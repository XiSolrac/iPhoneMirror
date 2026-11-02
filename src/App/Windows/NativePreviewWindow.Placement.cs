using System.Runtime.InteropServices;
using IPhoneMirror.App.Models;

namespace IPhoneMirror.App.Windows;

// Remembered placement per device, magnetic snapping to the work area's
// edges and corners after a move, and moving into a corner from the keyboard.
internal sealed partial class NativePreviewWindow
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const uint MonitorDefaultToNull = 0;
    private const double SnapDistanceDips = 24;
    private const double SnapMarginDips = 12;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmMoving = 0x0216;
    private PreviewPlacement? _initialPlacement;
    private PreviewControlsOverlayWindow? _controlsOverlay;

    /// <summary>Shows the dots and the floating controls over the preview.</summary>
    internal void AttachControlsOverlay(string udid, IPreviewOverlayActions actions)
    {
        if (_disposed || _handle == 0 || _managedContent is not null || _controlsOverlay is not null) return;
        _controlsOverlay = PreviewControlsOverlayWindow.ShowFor(_handle, udid, actions,
            () => _frameInsets);
    }

    private void CloseControlsOverlay()
    {
        _controlsOverlay?.Close();
        _controlsOverlay = null;
    }
    private WindowRect _sizeMoveStart;
    private bool _sizeMoveActive;
    // Where the pointer grabbed the window, to follow it past the top edge.
    private HoverPoint _sizeMoveCursor;
    private bool _sizeMoveCursorKnown;

    /// <summary>Raised after a move or resize, a pin change and before the window closes.</summary>
    internal event Action<PreviewPlacement>? PlacementChanged;

    /// <summary>Whether a finished move snaps to nearby screen edges.</summary>
    internal Func<bool>? SnapToEdgesEnabled { get; set; }

    /// <summary>Whether the window may grow past its screen and hang over an edge.</summary>
    internal Func<bool>? BeyondScreenEnabled { get; set; }

    /// <summary>The owner's view of whether Alt is held, for Alt+wheel resizing.</summary>
    internal Func<bool>? ResizeModifierPressed { get; set; }

    // Runs while the window is still hidden, so the first visible bounds are
    // the remembered ones. A monitor that is gone falls back to the default.
    private bool ApplyInitialPlacement()
    {
        if (_initialPlacement is not { IsUsable: true } placement) return false;
        var rect = new WindowRect
        {
            Left = placement.Left, Top = placement.Top,
            Right = placement.Left + placement.Width, Bottom = placement.Top + placement.Height,
        };
        if (MonitorFromRect(ref rect, MonitorDefaultToNull) == 0) return false;
        return SetWindowPos(_handle, 0, placement.Left, placement.Top,
            placement.Width, placement.Height, SwpNoZOrder | SwpNoActivate);
    }

    private void ObservePlacementMessage(int message)
    {
        if (message == WmWindowPosChanged)
        {
            _controlsOverlay?.SyncToOwner();
            return;
        }
        if (message == WmEnterSizeMove)
        {
            _sizeMoveActive = GetWindowRect(_handle, out _sizeMoveStart);
            _sizeMoveCursorKnown = GetCursorPos(out _sizeMoveCursor);
            _controlsOverlay?.SizeMoveChanged(true);
            return;
        }
        if (message != WmExitSizeMove) return;
        // Only a move snaps; a resize keeps the edge where the user left it.
        if (_sizeMoveActive && SnapToEdgesEnabled?.Invoke() != false &&
            GetWindowRect(_handle, out var end) &&
            end.Right - end.Left == _sizeMoveStart.Right - _sizeMoveStart.Left &&
            end.Bottom - end.Top == _sizeMoveStart.Bottom - _sizeMoveStart.Top)
            SnapToWorkArea(end, 0, 0, onlyWhenNear: true);
        _sizeMoveActive = false;
        _sizeMoveCursorKnown = false;
        _controlsOverlay?.SizeMoveChanged(false);
        PublishPlacement();
    }

    // Windows stops a moved window at the top of the screen so its caption
    // stays reachable. A preview that may hang past the edges follows the
    // pointer instead; the grabbed spot stays under it, so it stays reachable too.
    private bool FollowPointerWhileMoving(nint lParam)
    {
        if (!_sizeMoveActive || !_sizeMoveCursorKnown || lParam == 0 ||
            BeyondScreenEnabled?.Invoke() != true || !GetCursorPos(out var cursor)) return false;
        var rect = Marshal.PtrToStructure<WindowRect>(lParam);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        rect.Left = _sizeMoveStart.Left + cursor.X - _sizeMoveCursor.X;
        rect.Top = _sizeMoveStart.Top + cursor.Y - _sizeMoveCursor.Y;
        rect.Right = rect.Left + width;
        rect.Bottom = rect.Top + height;
        Marshal.StructureToPtr(rect, lParam, false);
        return true;
    }

    /// <summary>
    /// Moves the window into a corner of its screen. <paramref name="horizontal"/>
    /// is -1 for left and 1 for right, <paramref name="vertical"/> -1 for top and
    /// 1 for bottom; 0 keeps the nearer side.
    /// </summary>
    internal void MoveToCorner(int horizontal, int vertical)
    {
        if (_disposed || _handle == 0 || _isFullScreen || !GetWindowRect(_handle, out var rect)) return;
        SnapToWorkArea(rect, horizontal, vertical, onlyWhenNear: false);
        PublishPlacement();
        Log("independent_window_corner", ("mode", WindowMode),
            ("horizontal", horizontal), ("vertical", vertical));
    }

    private void SnapToWorkArea(WindowRect rect, int horizontal, int vertical, bool onlyWhenNear)
    {
        var monitor = MonitorFromWindow(_handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info)) return;
        var scale = Math.Max(96u, GetDpiForWindow(_handle)) / 96.0;
        var snap = (int)Math.Round(SnapDistanceDips * scale);
        var margin = (int)Math.Round(SnapMarginDips * scale);
        var mayOverhang = onlyWhenNear && BeyondScreenEnabled?.Invoke() == true;
        int Axis(int start, int size, int min, int max, int side)
        {
            var low = min + margin;
            var high = max - margin - size;
            if (high < low) return start; // larger than the screen: leave it
            if (side < 0) return low;
            if (side > 0) return high;
            if (!onlyWhenNear) return Math.Abs(start - low) <= Math.Abs(start - high) ? low : high;
            // Left hanging past an edge on purpose: only snap from inside.
            if (mayOverhang && (start < min || start + size > max)) return start;
            if (start - min <= snap + margin) return low;
            if (max - (start + size) <= snap + margin) return high;
            return start;
        }
        var left = Axis(rect.Left, rect.Right - rect.Left, info.WorkArea.Left, info.WorkArea.Right, horizontal);
        var top = Axis(rect.Top, rect.Bottom - rect.Top, info.WorkArea.Top, info.WorkArea.Bottom, vertical);
        if (left == rect.Left && top == rect.Top) return;
        _ = SetWindowPos(_handle, 0, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private void PublishPlacement()
    {
        if (_disposed || _handle == 0 || _isFullScreen || IsIconic(_handle) || IsZoomed(_handle) ||
            !GetWindowRect(_handle, out var rect)) return;
        PlacementChanged?.Invoke(new PreviewPlacement
        {
            Left = rect.Left, Top = rect.Top,
            Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top,
            TopMost = _isTopMost,
        });
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref WindowRect rect, uint flags);
}
