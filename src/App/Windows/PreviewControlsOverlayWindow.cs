using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;

namespace IPhoneMirror.App.Windows;

/// <summary>What the preview controls show for one device.</summary>
internal sealed record PreviewOverlayState(bool ControlsOnHoverOnly,
    PreviewDotPosition MoveDot, double MoveDotX, double MoveDotY,
    PreviewDotPosition ResizeDot, double ResizeDotX, double ResizeDotY);

/// <summary>Settings for the preview controls, supplied by the main window.</summary>
internal interface IPreviewOverlayActions
{
    PreviewOverlayState GetOverlayState(string udid, nint window);
}

/// <summary>
/// Transparent window over a native preview: a dot on the edge to move the
/// window, a dot to resize it. Both dots sit where the preview settings put
/// them, pulled onto the screen when the preview hangs past its edge.
/// Everything but those controls lets
/// clicks through to the preview, so touch control keeps working.
/// </summary>
internal sealed class PreviewControlsOverlayWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const nint HtClient = 1;
    private const nint HtTransparent = -1;
    private const int HtCaption = 2;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint MonitorDefaultToNearest = 2;
    // The dot's centre sits this far from the preview's edge, so the dot
    // touches the edge; its press area is a square around it.
    private const double DotEdgeInset = 7;
    private const double DotHitSize = 32;
    private const double DotSize = 12;
    private static readonly TimeSpan HoverLinger = TimeSpan.FromMilliseconds(900);
    private readonly nint _owner;
    private readonly string _udid;
    private readonly IPreviewOverlayActions _actions;
    private readonly Func<PreviewFrameInsets> _contentInset;
    private readonly DispatcherTimer _timer;
    private readonly List<FrameworkElement> _interactive = [];
    private HwndSource? _source;
    private DateTime _lastInside = DateTime.MinValue;
    // Physical bounds last given to this window, to skip unchanged moves.
    private WindowRect _applied;
    private bool _sizeMoving;
    private bool _hiddenForResize;
    private PreviewOverlayState? _state;

    private Dot _moveDot = null!;
    private Dot _resizeDot = null!;

    private PreviewControlsOverlayWindow(nint owner, string udid, IPreviewOverlayActions actions,
        Func<PreviewFrameInsets> contentInset)
    {
        _owner = owner;
        _udid = udid;
        _actions = actions;
        _contentInset = contentInset;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = BuildContent();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Refresh();
        // The dots follow a new size once layout has it.
        SizeChanged += (_, _) => PlaceDots();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    internal static PreviewControlsOverlayWindow ShowFor(nint owner, string udid,
        IPreviewOverlayActions actions, Func<PreviewFrameInsets> contentInset)
    {
        var overlay = new PreviewControlsOverlayWindow(owner, udid, actions, contentInset);
        var helper = new WindowInteropHelper(overlay) { Owner = owner };
        // Size the window over the preview before its first frame is drawn.
        helper.EnsureHandle();
        overlay.Refresh();
        overlay._timer.Start();
        return overlay;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private FrameworkElement BuildContent()
    {
        var root = new Grid();

        _moveDot = new Dot(Cursors.SizeAll, "PreviewMoveDot");
        _resizeDot = new Dot(Cursors.SizeNWSE, "PreviewResizeDot");
        foreach (var dot in new[] { _moveDot, _resizeDot })
        {
            // Hand the press to the preview window as a caption or edge press,
            // so Windows runs its own move/size loop (with snapping afterwards).
            dot.Root.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                ReleaseCapture();
                _ = SendMessageW(_owner, WmNcLeftButtonDown, dot.HitTest, 0);
            };
            _interactive.Add(dot.Root);
            root.Children.Add(dot.Root);
        }

        ApplyTexts();
        return root;
    }

    private void ApplyTexts()
    {
        foreach (var element in _interactive)
            if (element.Tag is string key) element.ToolTip = LocalizationService.Get(key);
    }

    /// <summary>Follows the preview window right away (called on its moves).</summary>
    internal void SyncToOwner() => UpdateBounds();

    /// <summary>
    /// The preview window entered or left its move/size loop. While it runs,
    /// the overlay only follows moves; a resize hides it until the end, since
    /// redrawing a transparent window at every step stalls that loop.
    /// </summary>
    internal void SizeMoveChanged(bool active)
    {
        _sizeMoving = active;
        if (active) return;
        _hiddenForResize = false;
        Refresh();
    }

    // Puts this window over the preview's content with one native call; a
    // plain move keeps the size, so nothing is redrawn.
    private bool UpdateBounds()
    {
        if (_source is null || _owner == 0 || !IsWindowVisible(_owner) || IsIconic(_owner) ||
            !GetWindowRect(_owner, out var rect))
        {
            if (IsVisible) Hide();
            return false;
        }
        var inset = _contentInset();
        if (rect.Right - rect.Left > inset.Horizontal && rect.Bottom - rect.Top > inset.Vertical)
        {
            rect.Left += inset.Left;
            rect.Top += inset.Top;
            rect.Right -= inset.Right;
            rect.Bottom -= inset.Bottom;
        }
        var sameSize = rect.Right - rect.Left == _applied.Right - _applied.Left &&
            rect.Bottom - rect.Top == _applied.Bottom - _applied.Top;
        if (_sizeMoving && (!sameSize || _hiddenForResize))
        {
            _hiddenForResize = true;
            if (IsVisible) Hide();
            return false;
        }
        // Showing puts a WPF window back on its own stored bounds, so show
        // first and compare with where this window really is, not with the
        // last bounds it was given.
        if (!IsVisible) Show();
        if (!GetWindowRect(_source.Handle, out var actual)) actual = default;
        var actualSameSize = rect.Right - rect.Left == actual.Right - actual.Left &&
            rect.Bottom - rect.Top == actual.Bottom - actual.Top;
        if (!actualSameSize || rect.Left != actual.Left || rect.Top != actual.Top)
        {
            var flags = SwpNoZOrder | SwpNoOwnerZOrder | SwpNoActivate | (actualSameSize ? SwpNoSize : 0);
            _ = SetWindowPos(_source.Handle, 0, rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top, flags);
        }
        _applied = rect;
        return true;
    }

    private void Refresh()
    {
        if (_sizeMoving || !UpdateBounds()) return;
        var state = _actions.GetOverlayState(_udid, _owner);
        // The whole preview counts, frame included, not just the phone's screen.
        if (GetCursorPos(out var cursor) && GetWindowRect(_owner, out var outer) &&
            cursor.X >= outer.Left && cursor.X < outer.Right &&
            cursor.Y >= outer.Top && cursor.Y < outer.Bottom)
            _lastInside = DateTime.UtcNow;
        var hovered = DateTime.UtcNow - _lastInside < HoverLinger;
        var controlsVisible = hovered || !state.ControlsOnHoverOnly;
        var controlsOpacity = hovered ? 1.0 : 0.55;

        _state = state;
        PlaceDots();
        SetShown(_moveDot.Root, controlsVisible, controlsOpacity);
        SetShown(_resizeDot.Root, controlsVisible, controlsOpacity);
    }

    private void PlaceDots()
    {
        if (_state is not { } state) return;
        var visible = VisibleArea();
        _moveDot.Place(PreviewDotPositions.Fractions(state.MoveDot, state.MoveDotX, state.MoveDotY),
            ActualWidth, ActualHeight, visible, resize: false);
        _resizeDot.Place(PreviewDotPositions.Fractions(state.ResizeDot, state.ResizeDotX, state.ResizeDotY),
            ActualWidth, ActualHeight, visible, resize: true);
    }

    // The part of this window on its screen's work area, in local DIPs. A
    // preview hanging past an edge keeps its dots there, where they can be reached.
    private (double Left, double Top, double Right, double Bottom) VisibleArea()
    {
        var whole = (0.0, 0.0, ActualWidth, ActualHeight);
        var physicalWidth = _applied.Right - _applied.Left;
        var physicalHeight = _applied.Bottom - _applied.Top;
        if (physicalWidth <= 0 || physicalHeight <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return whole;
        var monitor = MonitorFromWindow(_owner, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info)) return whole;
        var scaleX = ActualWidth / physicalWidth;
        var scaleY = ActualHeight / physicalHeight;
        var left = Math.Clamp((info.WorkArea.Left - _applied.Left) * scaleX, 0, ActualWidth);
        var top = Math.Clamp((info.WorkArea.Top - _applied.Top) * scaleY, 0, ActualHeight);
        var right = Math.Clamp((info.WorkArea.Right - _applied.Left) * scaleX, 0, ActualWidth);
        var bottom = Math.Clamp((info.WorkArea.Bottom - _applied.Top) * scaleY, 0, ActualHeight);
        return right - left < DotHitSize || bottom - top < DotHitSize ? whole : (left, top, right, bottom);
    }

    private static void SetShown(FrameworkElement element, bool shown, double opacity)
    {
        element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        element.Opacity = opacity;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // EnsureHandle raises this before the content is attached to the
        // source, so PresentationSource.FromVisual would still be null here.
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (_source is null) return;
        var style = GetWindowLongPtrW(_source.Handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtrW(_source.Handle, GwlExStyle, (nint)(style | WsExNoActivate | WsExToolWindow));
        _source.AddHook(WindowMessageHook);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyTexts();

    // Only the dots take the mouse; everything else passes to the
    // preview window below (same thread, so HTTRANSPARENT reaches it).
    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmNcHitTest || Content is not UIElement root) return 0;
        handled = true;
        var screen = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        Point local;
        try { local = root.PointFromScreen(screen); }
        catch (InvalidOperationException) { return HtTransparent; }
        foreach (var element in _interactive)
        {
            if (element.Visibility != Visibility.Visible) continue;
            var position = element.TranslatePoint(new Point(0, 0), root);
            if (local.X >= position.X && local.Y >= position.Y &&
                local.X < position.X + element.ActualWidth && local.Y < position.Y + element.ActualHeight)
                return HtClient;
        }
        return HtTransparent;
    }

    /// <summary>One dot: a small circle in a larger, invisible press area.</summary>
    private sealed class Dot
    {
        private readonly Ellipse _circle;
        private ((double X, double Y) Spot, double Width, double Height,
            (double Left, double Top, double Right, double Bottom) Visible) _placed = ((double.NaN, 0), 0, 0, default);

        internal Dot(Cursor cursor, string toolTipKey)
        {
            _circle = new Ellipse
            {
                Width = DotSize, Height = DotSize, Fill = Frozen(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)),
                Stroke = Frozen(Color.FromArgb(0x8C, 0x12, 0x12, 0x16)), StrokeThickness = 3,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            Root = new Grid
            {
                Width = DotHitSize, Height = DotHitSize, Background = Brushes.Transparent, Cursor = cursor,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Tag = toolTipKey, Children = { _circle },
            };
        }

        internal Grid Root { get; }

        /// <summary>The non-client code the press is handed to the preview window as.</summary>
        internal int HitTest { get; private set; } = HtCaption;

        // Places the circle at the spot (fractions of the preview, its centre
        // DotEdgeInset in from the edges), pulls it into the visible part and
        // keeps the press area inside that part.
        internal void Place((double X, double Y) spot, double width, double height,
            (double Left, double Top, double Right, double Bottom) visible, bool resize)
        {
            if (_placed == (spot, width, height, visible)) return;
            _placed = (spot, width, height, visible);
            var centreX = Math.Clamp(DotEdgeInset + spot.X * Math.Max(0, width - 2 * DotEdgeInset),
                visible.Left + DotEdgeInset, Math.Max(visible.Left + DotEdgeInset, visible.Right - DotEdgeInset));
            var centreY = Math.Clamp(DotEdgeInset + spot.Y * Math.Max(0, height - 2 * DotEdgeInset),
                visible.Top + DotEdgeInset, Math.Max(visible.Top + DotEdgeInset, visible.Bottom - DotEdgeInset));
            var left = Math.Clamp(centreX - DotHitSize / 2, visible.Left,
                Math.Max(visible.Left, visible.Right - DotHitSize));
            var top = Math.Clamp(centreY - DotHitSize / 2, visible.Top,
                Math.Max(visible.Top, visible.Bottom - DotHitSize));
            Root.Margin = new Thickness(left, top, 0, 0);
            _circle.Margin = new Thickness(centreX - left - DotSize / 2, centreY - top - DotSize / 2, 0, 0);
            if (!resize) return;
            // The resize dot pulls the nearest edge or corner; one in the
            // middle pulls the bottom right corner.
            var (horizontal, vertical) = PreviewDotPositions.Sides(spot.X, spot.Y);
            if (horizontal == 0 && vertical == 0) (horizontal, vertical) = (1, 1);
            (HitTest, Root.Cursor) = (horizontal, vertical) switch
            {
                (-1, -1) => (13, Cursors.SizeNWSE),
                (0, -1) => (12, Cursors.SizeNS),
                (1, -1) => (14, Cursors.SizeNESW),
                (-1, 0) => (10, Cursors.SizeWE),
                (1, 0) => (11, Cursors.SizeWE),
                (-1, 1) => (16, Cursors.SizeNESW),
                (0, 1) => (15, Cursors.SizeNS),
                _ => (17, Cursors.SizeNWSE),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal WindowRect Monitor;
        internal WindowRect WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
}
