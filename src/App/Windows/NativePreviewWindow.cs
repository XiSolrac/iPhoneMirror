using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using IPhoneMirror.App.Controls;
using IPhoneMirror.App.Interop;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.Windows;

/// <summary>
/// Native top-level DirectComposition preview. WS_EX_NOREDIRECTIONBITMAP keeps
/// DWM from allocating a second WPF redirection surface, allowing the native
/// renderer to attach its visual tree directly to this HWND.
/// </summary>
internal sealed partial class NativePreviewWindow : IDisposable
{
    internal const string StableTitle = "iPhoneMirror OBS Preview";
    private static readonly HashSet<NativePreviewWindow> OpenWindows = [];
    private static int BossKeyHidden;

    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmContextMenu = 0x007B;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcMouseLeave = 0x02A2;
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;
    private const int WmNcRightButtonDown = 0x00A4;
    private const int WmNcRightButtonUp = 0x00A5;
    private const int WmRightButtonDown = 0x0204;
    private const int WmRightButtonUp = 0x0205;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmClose = 0x0010;
    private const int WmEraseBackground = 0x0014;
    private const int WmMouseMove = 0x0200;
    private const uint PmRemove = 0x0001;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMiddleButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmSetCursor = 0x0020;
    private const int WmKillFocus = 0x0008;
    private const int WmCancelMode = 0x001F;
    private const int WmActivateApp = 0x001C;
    private const int WmActivate = 0x0006;
    private const int WmCaptureChanged = 0x0215;
    private const int WmSetIcon = 0x0080;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmDpiChanged = 0x02E0;
    private const int WmDropFiles = 0x0233;
    private const int WmCopyData = 0x004A;
    private const int WmCopyGlobalData = 0x0049;
    private const uint MsgFltAllow = 1;
    private const int VkEscape = 0x1B;
    private const int VkReturn = 0x0D;
    private const int VkF11 = 0x7A;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    // Size change per wheel notch with Alt held.
    private const double WheelScaleStep = 1.1;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsCaption = 0x00C00000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsThickFrame = 0x00040000;
    private const int WsSysMenu = 0x00080000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExNoRedirectionBitmap = 0x00200000;

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint MonitorDefaultToNearest = 2;
    private const uint CursorShowing = 0x00000001;
    private static readonly nint HwndTopMost = new(-1);
    private static readonly nint HwndNoTopMost = new(-2);

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmDoNotRound = 1;
    private const int DwmRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;

    private readonly HwndSource _source;
    private readonly AspectRatioWindowController _aspectController;
    private Func<nint, bool> _attachPreview;
    private Action<nint> _detachPreview;
    private Func<nint, bool> _refreshPreview;
    private readonly ContextMenu _contextMenu;
    private readonly MenuItem _fullScreenItem;
    private readonly MenuItem _windowMenuItem;
    private readonly MenuItem _displayMenuItem;
    private readonly MenuItem _topMostItem;
    private readonly MenuItem _fixedItem;
    private readonly MenuItem? _frameItem;
    private readonly MenuItem? _previewSettingsItem;
    private readonly MenuItem _styleItem;
    private readonly MenuItem? _reverseControlMenuItem;
    private readonly MenuItem? _bluetoothControlItem;
    private readonly MenuItem? _usbControlItem;
    private readonly MenuItem? _wirelessControlItem;
    private readonly MenuItem? _cornerItem;
    private readonly MenuItem _muteMenuItem;
    private readonly MenuItem _muteThisItem;
    private readonly MenuItem _muteOthersItem;
    private readonly MenuItem _rotateLeftItem;
    private readonly MenuItem _rotateRightItem;
    private readonly MenuItem _closeItem;
    private readonly MenuItem? _imageSettingsItem;
    private readonly MenuItem? _projectionSettingsItem;
    private readonly Func<bool>? _isAudioEnabled;
    private readonly Func<int>? _connectedDeviceCount;
    private readonly Action<bool>? _setAudioEnabled;
    private readonly Action? _muteOtherWindows;
    private readonly Action<nint>? _showImageSettings;
    private readonly Action? _showProjectionSettings;
    private readonly Func<nint, bool>? _isReverseControlEnabled;
    private readonly Action<PreviewPointerEventArgs>? _pointerInput;
    private readonly Action<PreviewKeyboardEventArgs>? _keyboardInput;
    private readonly Action<nint>? _requestReverseControl;
    private readonly Func<bool>? _isUsbControlEnabled;
    private readonly Action<nint>? _requestUsbControl;
    private readonly Action<nint>? _requestWirelessControl;
    private readonly Action<string>? _logDiagnostic;
    private ulong _sessionHandle;
    private readonly double _cornerRadius;
    private readonly double _cornerExponent;
    private readonly Border? _managedContentRoot;
    private readonly FrameworkElement? _managedContent;
    private readonly Transform? _managedContentOriginalLayoutTransform;
    private readonly Action? _managedContentDetached;
    private uint _sourceWidth;
    private uint _sourceHeight;
    private nint _handle;
    private bool _attached;
    private bool _isFullScreen;
    private bool _disposed;
    private bool _closeQueued;
    private bool _isTopMost;
    private bool _isFixed;
    private bool _cornersEnabled = true;
    private int _rotation;
    // Physical-pixel outline between the client edges and the video; empty
    // when the frame is hidden, unsupported, or the window is full screen.
    private PreviewFrameInsets _frameInsets;
    private PreviewFrameSettings _frameSettings = new();
    private bool _homeHover;
    private string? _lastFrameError;
    private byte _capturedMouseButtons;
    private nint _largeIcon;
    private nint _smallIcon;
    private ProtectedContentOverlayWindow? _protectedOverlay;
    private WindowStyleSettingsWindow? _styleWindow;
    private double _windowOpacity = 1.0;
    private double _appliedOpacity = 1.0;
    private bool _opaqueOnHover;
    private bool _isPointerOverWindow;
    private readonly DispatcherTimer _hoverOpacityTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(80),
    };
    private WindowRect _restoreRectangle;
    private nint _restoreStyle;

    internal bool IsFullScreen => _isFullScreen;
    internal event Action<bool>? FullScreenChanged;

    private NativePreviewWindow(uint sourceWidth, uint sourceHeight, string title,
        Func<nint, bool> attachPreview, Action<nint> detachPreview,
        Func<nint, bool> refreshPreview, ulong sessionHandle,
         double cornerRadius, double cornerExponent,
         Func<bool>? isAudioEnabled = null, Func<int>? connectedDeviceCount = null,
         Action<bool>? setAudioEnabled = null, Action? muteOtherWindows = null,
          Action<nint>? showImageSettings = null, Action? showProjectionSettings = null,
           Func<nint, bool>? isReverseControlEnabled = null,
           Action<PreviewPointerEventArgs>? pointerInput = null,
           Action<PreviewKeyboardEventArgs>? keyboardInput = null,
           Action<nint>? requestReverseControl = null,
           Func<bool>? isUsbControlEnabled = null,
           Action<nint>? requestUsbControl = null,
           Action<nint>? requestWirelessControl = null,
           FrameworkElement? managedContent = null, Action? managedContentDetached = null,
         Action<string>? logDiagnostic = null)
    {
        _attachPreview = attachPreview;
        _detachPreview = detachPreview;
        _refreshPreview = refreshPreview;
        _sessionHandle = sessionHandle;
        _cornerRadius = cornerRadius;
        _cornerExponent = cornerExponent;
        _isAudioEnabled = isAudioEnabled;
        _connectedDeviceCount = connectedDeviceCount;
        _setAudioEnabled = setAudioEnabled;
        _muteOtherWindows = muteOtherWindows;
        _showImageSettings = showImageSettings;
        _showProjectionSettings = showProjectionSettings;
        _isReverseControlEnabled = isReverseControlEnabled;
        _pointerInput = pointerInput;
        _keyboardInput = keyboardInput;
        _requestReverseControl = requestReverseControl;
        _isUsbControlEnabled = isUsbControlEnabled;
        _requestUsbControl = requestUsbControl;
        _requestWirelessControl = requestWirelessControl;
        _logDiagnostic = logDiagnostic;
        _managedContent = managedContent;
        _managedContentOriginalLayoutTransform = managedContent?.LayoutTransform;
        _managedContentDetached = managedContentDetached;
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _hoverOpacityTimer.Tick += (_, _) => UpdateHoverState(IsPointerOverPreview());
        _contextMenu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("DeviceContextMenuStyle"),
            Placement = PlacementMode.MousePoint,
        };
        var itemStyle = (Style)Application.Current.FindResource("DeviceMenuItemStyle");
        var submenuStyle = (Style)Application.Current.FindResource("DeviceSubmenuItemStyle");
        _fullScreenItem = new MenuItem { Style = itemStyle };
        _fullScreenItem.Click += (_, _) => ToggleFullScreen();
        _windowMenuItem = new MenuItem { Style = submenuStyle };
        _topMostItem = new MenuItem { Style = itemStyle };
        _topMostItem.Click += (_, _) => ToggleTopMost();
        _fixedItem = new MenuItem { Style = itemStyle };
        _fixedItem.Click += (_, _) => ToggleFixedWindow();
        _styleItem = new MenuItem { Style = itemStyle };
        _styleItem.Click += (_, _) => ShowStyleSettings();
        if (_requestReverseControl is not null)
        {
            _reverseControlMenuItem = new MenuItem { Style = submenuStyle };
            _bluetoothControlItem = new MenuItem { Style = itemStyle };
            _bluetoothControlItem.Click += (_, _) => EnableReverseControl();
            _reverseControlMenuItem.Items.Add(_bluetoothControlItem);
        }
        if (_requestUsbControl is not null)
        {
            _usbControlItem = new MenuItem { Style = itemStyle };
            _usbControlItem.Click += (_, _) => _requestUsbControl(_handle);
            _reverseControlMenuItem?.Items.Add(_usbControlItem);
        }
        if (_requestWirelessControl is not null)
        {
            _wirelessControlItem = new MenuItem { Style = itemStyle };
            _wirelessControlItem.Click += (_, _) => _requestWirelessControl(_handle);
            _reverseControlMenuItem?.Items.Add(_wirelessControlItem);
        }
        _windowMenuItem.Items.Add(_topMostItem);
        _windowMenuItem.Items.Add(_fixedItem);
        if (managedContent is null)
        {
            _frameItem = new MenuItem { Style = itemStyle };
            _frameItem.Click += (_, _) => PreviewFrameService.Update(
                settings => settings.Enabled = !settings.Enabled);
            _windowMenuItem.Items.Add(_frameItem);
            _previewSettingsItem = new MenuItem { Style = itemStyle };
            _previewSettingsItem.Click += (_, _) => PreviewSettingsWindow.ShowOrActivate(_handle);
            _windowMenuItem.Items.Add(_previewSettingsItem);
        }
        _windowMenuItem.Items.Add(_styleItem);
        _displayMenuItem = new MenuItem { Style = submenuStyle };
        _muteMenuItem = new MenuItem { Style = submenuStyle };
        _muteThisItem = new MenuItem { Style = itemStyle };
        _muteThisItem.Click += (_, _) => ToggleWindowAudio();
        _muteOthersItem = new MenuItem { Style = itemStyle };
        _muteOthersItem.Click += (_, _) => MuteOtherWindows();
        _muteMenuItem.Items.Add(_muteThisItem);
        _muteMenuItem.Items.Add(_muteOthersItem);
        _rotateLeftItem = new MenuItem { Style = itemStyle };
        _rotateLeftItem.Click += (_, _) => Rotate(-1);
        _rotateRightItem = new MenuItem { Style = itemStyle };
        _rotateRightItem.Click += (_, _) => Rotate(1);
        _closeItem = new MenuItem { Style = itemStyle };
        _closeItem.Click += (_, _) => QueueClose();
        if (managedContent is null)
        {
            _cornerItem = new MenuItem { Style = itemStyle };
            _cornerItem.Click += (_, _) => ToggleCorners();
            if (_showImageSettings is not null)
            {
                _imageSettingsItem = new MenuItem { Style = itemStyle };
                _imageSettingsItem.Click += (_, _) => ShowImageSettings();
                _displayMenuItem.Items.Add(_imageSettingsItem);
            }
            _displayMenuItem.Items.Add(_cornerItem);
            if (_showProjectionSettings is not null)
            {
                _projectionSettingsItem = new MenuItem { Style = itemStyle };
                _projectionSettingsItem.Click += (_, _) => ShowProjectionSettings();
            }
        }
        _displayMenuItem.Items.Add(_rotateLeftItem);
        _displayMenuItem.Items.Add(_rotateRightItem);
        _contextMenu.Items.Add(_fullScreenItem);
        _contextMenu.Items.Add(_windowMenuItem);
        _contextMenu.Items.Add(_displayMenuItem);
        if (_reverseControlMenuItem is not null) _contextMenu.Items.Add(_reverseControlMenuItem);
        if (_setAudioEnabled is not null) _contextMenu.Items.Add(_muteMenuItem);
        if (_projectionSettingsItem is not null) _contextMenu.Items.Add(_projectionSettingsItem);
        _contextMenu.Items.Add(new Separator
        {
            Style = (Style)Application.Current.FindResource("DeviceMenuSeparatorStyle"),
        });
        _contextMenu.Items.Add(_closeItem);
        UpdateContextMenuLabels();
        var windowTitle = string.IsNullOrWhiteSpace(title) ? StableTitle : title;
        var parameters = new HwndSourceParameters(windowTitle)
        {
            Width = 720,
            Height = 900,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = managedContent is null
                ? WsPopup | WsThickFrame | WsSysMenu | WsMinimizeBox |
                    WsClipChildren | WsClipSiblings
                : WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox |
                    WsMaximizeBox | WsClipChildren | WsClipSiblings,
            ExtendedWindowStyle = WsExAppWindow |
                (managedContent is null ? WsExNoRedirectionBitmap : 0),
            TreatAsInputRoot = true,
        };
        _source = new HwndSource(parameters);
        _handle = _source.Handle;
        if (_handle == 0) throw new InvalidOperationException(
            "Could not create the native preview window.");
        if (managedContent is null)
        {
            NativeTouchInput.RegisterTouchWindow(_handle);
            // Files dragged from Explorer go to the iPhone clipboard. Let the
            // drop messages through when an elevated build runs.
            foreach (var message in new[] { WmDropFiles, WmCopyData, WmCopyGlobalData })
                _ = ChangeWindowMessageFilterEx(_handle, (uint)message, MsgFltAllow, 0);
            DragAcceptFiles(_handle, true);
        }

        if (managedContent is not null)
        {
            _managedContentRoot = new Border
            {
                Background = Brushes.Black,
                Child = managedContent,
                ClipToBounds = true,
            };
            _source.RootVisual = _managedContentRoot;
        }

        _ = SetWindowTextW(_handle, windowTitle);
        ApplyApplicationIcons();
        if (managedContent is null)
        {
            var cornerPreference = DwmDoNotRound;
            _ = DwmSetWindowAttribute(_handle, DwmWindowCornerPreference,
                ref cornerPreference, sizeof(int));
            var borderColor = DwmColorNone;
            _ = DwmSetWindowAttributeColor(_handle, DwmBorderColor,
                ref borderColor, sizeof(uint));
        }
        else
        {
            // Media-cast content uses a normal captioned HWND. Explicitly ask
            // DWM for the Windows default corner treatment so it remains
            // rounded even when the app's theme changes window preferences.
            var cornerPreference = DwmRound;
            _ = DwmSetWindowAttribute(_handle, DwmWindowCornerPreference,
                ref cornerPreference, sizeof(int));
            var darkTitleBar = ThemeService.IsDark ? 1 : 0;
            _ = DwmSetWindowAttribute(_handle, DwmUseImmersiveDarkMode,
                ref darkTitleBar, sizeof(int));
        }

        _aspectController = new AspectRatioWindowController(_source,
            sourceWidth, sourceHeight,
            () => !_disposed && !_isFullScreen && _handle != 0 &&
                !IsIconic(_handle) && !IsZoomed(_handle),
            minWidthDips: 96, minHeightDips: 96,
            contentInset: () => (_frameInsets.Horizontal, _frameInsets.Vertical),
            allowBeyondWorkArea: () => BeyondScreenEnabled?.Invoke() == true);
        // Install the instance hook only after every callback dependency is
        // initialized; HwndSource construction itself dispatches messages.
        _source.AddHook(WindowProcedure);
        OpenWindows.Add(this);
        ThemeService.ThemeChanged += OnThemeChanged;
        if (managedContent is null) PreviewFrameService.Changed += OnPreviewFrameChanged;
        if (managedContent is null)
        {
            // Preserve the original borderless DirectComposition window for
            // wired/wireless mirroring. Video casting alone uses system chrome.
            _ = SetWindowPos(_handle, 0, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        Log("independent_window_created",
            ("mode", WindowMode), ("handle", AppLog.Handle(_sessionHandle)),
            ("size", $"{sourceWidth}x{sourceHeight}"),
            ("title_bar", managedContent is not null));
    }

    internal event EventHandler? Closed;
    internal event Action<bool>? ActivationChanged;
    // Raised on the UI thread when the home pill in the frame is clicked.
    internal event Action? HomeRequested;
    // Raised on the UI thread with the paths of files dropped on the window.
    internal event Action<string[]>? FilesDropped;
    private bool _previewHidden;
    private bool _appHidden;
    private bool _restoreVisible;
    private bool _protectedContent;
    private string _protectedAudioDisplay = string.Empty;
    internal static event Action? VisibilityChanged;
    internal bool IsVisibilityHidden => _previewHidden || _appHidden;
    internal bool IsPreviewHidden => _previewHidden;
    internal bool IsLive => !_disposed && _handle != 0;
    internal ulong SessionHandle => _sessionHandle;
    internal nint Handle => _handle;
    internal (uint Width, uint Height, int Rotation) ControlGeometry =>
        (_rotation & 1) == 0
            ? (_sourceWidth, _sourceHeight, _rotation)
            : (_sourceHeight, _sourceWidth, _rotation);

    internal bool RebindSession(ulong sessionHandle)
    {
        if (_disposed || _managedContent is not null || _handle == 0 || sessionHandle == 0)
            return false;

        var previousHandle = _sessionHandle;
        if (_attached)
        {
            try { _detachPreview(_handle); }
            catch { /* The old native session may already be gone. */ }
            _attached = false;
        }

        _sessionHandle = sessionHandle;
        if (previousHandle != sessionHandle) SetPreviewHidden(false);
        VisibilityChanged?.Invoke();
        _attachPreview = hwnd => NativeCore.AttachDevicePreview(sessionHandle, hwnd);
        _detachPreview = hwnd => NativeCore.DetachDevicePreview(sessionHandle, hwnd);
        _refreshPreview = hwnd => NativeCore.AttachDevicePreview(sessionHandle, hwnd);
        if (!_attachPreview(_handle))
        {
            Log("independent_window_rebind_failed",
                ("mode", WindowMode), ("old_handle", AppLog.Handle(previousHandle)),
                ("new_handle", AppLog.Handle(sessionHandle)));
            return false;
        }

        _attached = true;
        _ = NativeCore.SetDeviceWindowCornerProfile(sessionHandle, _handle,
            _cornersEnabled ? _cornerRadius : 0, _cornerExponent);
        if (_rotation != 0)
            _ = NativeCore.SetDeviceWindowRotation(sessionHandle, _handle, _rotation);
        ApplyFrame();
        _ = ApplyWindowOpacity(EffectiveWindowOpacity, force: true);
        Log("independent_window_rebound",
            ("mode", WindowMode), ("old_handle", AppLog.Handle(previousHandle)),
            ("new_handle", AppLog.Handle(sessionHandle)));
        return true;
    }

    private string WindowMode => _managedContent is null ? "device" : "media_cast";

    private void Log(string eventName, params (string Key, object? Value)[] fields)
    {
        try
        {
            _logDiagnostic?.Invoke(AppLog.Event(eventName,
                fields.Select(field => (object?)field).ToArray()));
        }
        catch (Exception error)
        {
            // Diagnostics must never interfere with HWND ownership or teardown.
            DiagnosticLogger.ExceptionOnce("preview-log-callback", "logging",
                "preview_log_callback_failed", error);
        }
    }

    internal static bool TryCreateAndShowForSession(ulong handle, uint sourceWidth,
        uint sourceHeight, string title, double cornerRadius, double cornerExponent,
        Func<bool> isAudioEnabled, Func<int> connectedDeviceCount,
        Action<bool> setAudioEnabled, Action muteOtherWindows,
        Action<nint> showImageSettings, Action showProjectionSettings,
        out NativePreviewWindow? window, Action<string>? logDiagnostic = null,
        Func<nint, bool>? isReverseControlEnabled = null,
        Action<PreviewPointerEventArgs>? pointerInput = null,
        Action<PreviewKeyboardEventArgs>? keyboardInput = null,
        Action<nint>? requestReverseControl = null,
        Func<bool>? isUsbControlEnabled = null,
        Action<nint>? requestUsbControl = null,
        Action<nint>? requestWirelessControl = null,
        PreviewPlacement? placement = null)
    {
        window = null;
        NativePreviewWindow? candidate = null;
        try
        {
            candidate = new NativePreviewWindow(sourceWidth, sourceHeight, title,
                hwnd => NativeCore.AttachDevicePreview(handle, hwnd),
                hwnd => NativeCore.DetachDevicePreview(handle, hwnd),
                hwnd => NativeCore.AttachDevicePreview(handle, hwnd),
                 handle, cornerRadius, cornerExponent, isAudioEnabled,
                 connectedDeviceCount, setAudioEnabled, muteOtherWindows,
                  showImageSettings, showProjectionSettings,
                  isReverseControlEnabled, pointerInput, keyboardInput, requestReverseControl,
                  isUsbControlEnabled, requestUsbControl,
                  requestWirelessControl,
                  logDiagnostic: logDiagnostic);
            if (!candidate._attachPreview(candidate._handle))
            {
                logDiagnostic?.Invoke(AppLog.Event("independent_window_attach_failed",
                    ("mode", "device"), ("handle", AppLog.Handle(handle))));
                candidate.Dispose();
                return false;
            }
            candidate._attached = true;
            // Apply the frame while hidden so the first visible bounds
            // already include it.
            candidate.ApplyFrame(reflow: false);
            candidate._initialPlacement = placement;
            candidate.ShowInitially();
            if (Volatile.Read(ref BossKeyHidden) != 0)
                candidate.SetBossKeyHidden(true);
            else
                _ = SetForegroundWindow(candidate._handle);
            window = candidate;
            logDiagnostic?.Invoke(AppLog.Event("independent_window_shown",
                ("mode", "device"), ("handle", AppLog.Handle(handle))));
            return true;
        }
        catch (Exception error)
        {
            logDiagnostic?.Invoke(AppLog.Event("independent_window_create_failed",
                ("mode", "device"), ("handle", AppLog.Handle(handle)),
                ("error", AppLog.Error(error))));
            candidate?.Dispose();
            return false;
        }
    }

    internal static bool TryCreateAndShowForContent(FrameworkElement content,
        uint sourceWidth, uint sourceHeight, string title,
        Func<bool> isAudioEnabled, Action<bool> setAudioEnabled,
        Func<int> connectedDeviceCount, Action muteOtherWindows,
        Action contentDetached, out NativePreviewWindow? window,
        Action<string>? logDiagnostic = null)
    {
        window = null;
        NativePreviewWindow? candidate = null;
        try
        {
            candidate = new NativePreviewWindow(sourceWidth, sourceHeight, title,
                _ => true, _ => { }, _ =>
                 {
                     content.InvalidateVisual();
                     return true;
                 }, 0, 0, 1, isAudioEnabled, connectedDeviceCount, setAudioEnabled,
                 muteOtherWindows, null, null,
                 managedContent: content, managedContentDetached: contentDetached,
                 logDiagnostic: logDiagnostic);
            candidate._attached = true;
            candidate.ShowInitially();
            if (Volatile.Read(ref BossKeyHidden) != 0)
                candidate.SetBossKeyHidden(true);
            else
                _ = SetForegroundWindow(candidate._handle);
            window = candidate;
            logDiagnostic?.Invoke(AppLog.Event("independent_window_shown",
                ("mode", "media_cast"), ("handle", AppLog.Handle(0))));
            return true;
        }
        catch (Exception error)
        {
            logDiagnostic?.Invoke(AppLog.Event("independent_window_create_failed",
                ("mode", "media_cast"), ("error", AppLog.Error(error))));
            if (candidate is not null) candidate.Dispose();
            else
            {
                if (content.Parent is Decorator owner) owner.Child = null;
                contentDetached();
            }
            return false;
        }
    }

    private void ShowInitially()
    {
        var boundsApplied = ApplyInitialPlacement() || _aspectController.ApplyInitialBounds();
        _isTopMost = _initialPlacement?.TopMost == true;
        UpdateContextMenuLabels();
        if (Volatile.Read(ref BossKeyHidden) != 0)
        {
            _appHidden = true;
            _restoreVisible = true;
            return;
        }
        var zOrder = _isTopMost ? HwndTopMost : HwndNoTopMost;
        if (!SetWindowPos(_handle, zOrder, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow))
        {
            // Keep the window usable if the combined show/z-order operation is
            // rejected by the shell. Its final bounds were already applied
            // while hidden, so this fallback still cannot expose a move.
            _ = ShowWindow(_handle, SwShow);
            _ = SetWindowPos(_handle, zOrder, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoActivate);
        }
        Log("independent_window_initial_layout",
            ("mode", WindowMode), ("bounds_applied", boundsApplied),
            ("top_most", _isTopMost));
    }

    internal void Activate()
    {
        if (_disposed || _handle == 0 || IsVisibilityHidden) return;
        if (IsIconic(_handle)) _ = ShowWindow(_handle, SwRestore);
        else _ = ShowWindow(_handle, SwShow);
        if (!_attached && _attachPreview(_handle))
            _attached = true;
        else if (_attached)
            _ = _attachPreview(_handle);
        if (_windowOpacity < 1.0) _ = ApplyWindowOpacity(EffectiveWindowOpacity, force: true);
        _ = SetForegroundWindow(_handle);
        _ = SetFocus(_handle);
        Log("independent_window_activated",
            ("mode", WindowMode), ("attached", _attached),
            ("full_screen", _isFullScreen));
    }

    internal void HideForShutdown()
    {
        if (_disposed || _handle == 0) return;
        _styleWindow?.Close();
        _contextMenu.IsOpen = false;
        _contextMenu.PlacementTarget = null;
        _ = ShowWindow(_handle, SwHide);
    }

    internal void SetPreviewHidden(bool hidden) => SetVisibilityHidden(hidden, app: false);

    internal void SetBossKeyHidden(bool hidden) => SetVisibilityHidden(hidden, app: true);

    private void SetVisibilityHidden(bool hidden, bool app)
    {
        if (!IsLive || (app ? _appHidden : _previewHidden) == hidden) return;
        var wasHidden = IsVisibilityHidden;
        if (!wasHidden && hidden) _restoreVisible = IsWindowVisible(_handle);
        if (app) _appHidden = hidden;
        else _previewHidden = hidden;
        if (!wasHidden && IsVisibilityHidden)
        {
            _contextMenu.IsOpen = false;
            _contextMenu.PlacementTarget = null;
            _capturedMouseButtons = 0;
            if (GetCapture() == _handle) _ = ReleaseCapture();
            _pointerInput?.Invoke(new PreviewPointerEventArgs(PreviewPointerKind.Reset, 0, 0, 0, 0));
            _keyboardInput?.Invoke(new PreviewKeyboardEventArgs(PreviewKeyboardKind.Reset, 0));
            _protectedOverlay?.Hide();
            _ = ShowWindow(_handle, SwHide);
        }
        else if (wasHidden && !IsVisibilityHidden && _restoreVisible)
        {
            // SW_SHOWNA keeps the current minimized/maximized/fullscreen placement.
            _ = ShowWindow(_handle, 8);
            SetProtectedContent(_protectedContent, _protectedAudioDisplay);
            if (_protectedOverlay is not null) _protectedOverlay.Show();
        }
        VisibilityChanged?.Invoke();
    }

    internal static void SetAllBossKeyHidden(bool hidden)
    {
        Volatile.Write(ref BossKeyHidden, hidden ? 1 : 0);
        foreach (var window in OpenWindows.ToArray())
            window.SetBossKeyHidden(hidden);
    }

    internal bool RefreshPreview()
    {
        var refreshed = !_disposed && _handle != 0 && _refreshPreview(_handle);
        if (refreshed && _windowOpacity < 1.0) _ = ApplyWindowOpacity(EffectiveWindowOpacity, force: true);
        Log("independent_window_refresh",
            ("mode", WindowMode), ("success", refreshed));
        return refreshed;
    }

    internal void SetProtectedContent(bool protectedContent, string audioDisplay)
    {
        if (_disposed || _handle == 0 || _managedContent is not null) return;
        _protectedContent = protectedContent;
        _protectedAudioDisplay = audioDisplay;
        if (!protectedContent)
        {
            _protectedOverlay?.Close();
            _protectedOverlay = null;
            return;
        }
        if (IsVisibilityHidden) return;
        if (_protectedOverlay is null)
            _protectedOverlay = ProtectedContentOverlayWindow.ShowFor(_handle,
                audioDisplay, () => _frameInsets);
        else
            _protectedOverlay.UpdateAudioDisplay(audioDisplay);
        _protectedOverlay.Opacity = _appliedOpacity;
    }

    internal void SetSourceDimensions(uint width, uint height)
    {
        var changed = _sourceWidth != width || _sourceHeight != height;
        if (changed) _pointerInput?.Invoke(new PreviewPointerEventArgs(PreviewPointerKind.Reset, 0, 0, 0, 0));
        _sourceWidth = width;
        _sourceHeight = height;
        ApplyRotatedDimensions();
        if (changed)
            Log("independent_window_dimensions",
                ("mode", WindowMode), ("size", $"{width}x{height}"),
                ("rotation", _rotation));
    }

    internal void ToggleFullScreen()
    {
        if (_disposed || _handle == 0) return;
        if (_isFullScreen)
        {
            _isFullScreen = false;
            _ = RemovePropW(_handle, "iPhoneMirrorFullScreen");
            _ = SetWindowLongPtrW(_handle, GwlStyle, _restoreStyle);
            _ = SetWindowPos(_handle, 0, _restoreRectangle.Left, _restoreRectangle.Top,
                Math.Max(1, _restoreRectangle.Right - _restoreRectangle.Left),
                Math.Max(1, _restoreRectangle.Bottom - _restoreRectangle.Top),
                SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
            ApplyFrame(reflow: false);
            _aspectController.Reflow();
            _ = SetForegroundWindow(_handle);
            Log("independent_window_fullscreen",
                ("mode", WindowMode), ("enabled", false));
            FullScreenChanged?.Invoke(false);
            return;
        }

        if (!GetWindowRect(_handle, out _restoreRectangle)) return;
        var monitor = MonitorFromWindow(_handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref monitorInfo)) return;

        _restoreStyle = GetWindowLongPtrW(_handle, GwlStyle);
        _isFullScreen = true;
        _ = SetPropW(_handle, "iPhoneMirrorFullScreen", (nint)1);
        var fullScreenStyle = _managedContent is null
            ? (nint)(_restoreStyle.ToInt64() & ~WsThickFrame)
            : (nint)(_restoreStyle.ToInt64() & ~(WsCaption | WsThickFrame));
        _ = SetWindowLongPtrW(_handle, GwlStyle, fullScreenStyle);
        ApplyFrame(reflow: false);
        _ = SetWindowPos(_handle, 0, monitorInfo.Monitor.Left, monitorInfo.Monitor.Top,
            monitorInfo.Monitor.Right - monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top,
            SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
        _ = SetForegroundWindow(_handle);
        Log("independent_window_fullscreen",
            ("mode", WindowMode), ("enabled", true));
        FullScreenChanged?.Invoke(true);
    }

    private void ApplyApplicationIcons()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            ExtractIconExW(executable, 0, out _largeIcon, out _smallIcon, 1) == 0)
            return;
        if (_largeIcon != 0) _ = SendMessageW(_handle, WmSetIcon, 1, _largeIcon);
        if (_smallIcon != 0) _ = SendMessageW(_handle, WmSetIcon, 0, _smallIcon);
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam,
        ref bool handled)
    {
        if (message is WmEnterSizeMove or WmExitSizeMove or WmWindowPosChanged) ObservePlacementMessage(message);
        if (message == WmMoving && FollowPointerWhileMoving(lParam))
        {
            handled = true;
            return 1;
        }
        if (message == WmDropFiles)
        {
            var files = ReadDroppedFiles(wParam);
            if (files.Length > 0) FilesDropped?.Invoke(files);
            handled = true;
            return 0;
        }
        if (message == NativeTouchInput.WmTouch && _managedContent is null)
        {
            GetClientRect(hwnd, out var touchRect);
            NativeTouchInput.Dispatch(hwnd, wParam, lParam, IsPointerInputActive && IsUsbControlEnabledForWindow,
                Math.Max(1, touchRect.Right - touchRect.Left - _frameInsets.Horizontal),
                Math.Max(1, touchRect.Bottom - touchRect.Top - _frameInsets.Vertical),
                (_rotation & 1) == 0 ? _sourceWidth : _sourceHeight,
                (_rotation & 1) == 0 ? _sourceHeight : _sourceWidth, _rotation,
                input => _pointerInput?.Invoke(input), _frameInsets.Left, _frameInsets.Top);
            handled = true;
            return 0;
        }
        if (IsPointerInputActive && IsUsbControlEnabledForWindow && _managedContent is null && NativeTouchInput.IsPromotedMouseMessage(message))
        {
            handled = true;
            return 0;
        }
        if (message == 0x007C && wParam.ToInt32() == GwlExStyle &&
            lParam != 0 && _managedContent is not null) // WM_STYLECHANGING
        {
            // HwndTarget normally strips WS_EX_LAYERED when not using per-pixel
            // alpha. This host owns system-managed constant alpha, including the
            // native caption. Preserve it across WPF resize/target updates.
            var style = Marshal.ReadInt32(lParam, sizeof(int));
            style = _appliedOpacity < 1.0 ? style | WsExLayered : style & ~WsExLayered;
            Marshal.WriteInt32(lParam, sizeof(int), style);
            handled = true;
            return 0;
        }
        if (message == WmActivate)
            ActivationChanged?.Invoke(!IsVisibilityHidden && (wParam.ToInt64() & 0xFFFF) != 0);
        if (WindowsAutoPlayGuard.ShouldCancel(message, _sessionHandle != 0))
        {
            handled = true;
            Log("autoplay_cancelled", ("message", "WM_QUERYCANCELAUTOPLAY"));
            return 1;
        }
        // A normal button-up releases capture after clearing the button mask.
        // Keep queued shortcuts and held keyboard modifiers alive in that case.
        if (message == WmCaptureChanged && _capturedMouseButtons == 0)
        {
            handled = true;
            return 0;
        }
        if (IsPointerInputEnabledForWindow &&
            (message == WmKillFocus || message == WmCancelMode ||
             (message == WmActivate && (wParam.ToInt64() & 0xFFFF) == 0) ||
             message == WmCaptureChanged ||
             (message == WmActivateApp && wParam == 0)))
        {
            if (_capturedMouseButtons != 0)
            {
                _capturedMouseButtons = 0;
                if (GetCapture() == hwnd) _ = ReleaseCapture();
            }
            _pointerInput?.Invoke(new PreviewPointerEventArgs(
                PreviewPointerKind.Reset, 0, 0, 0, 0));
            _keyboardInput?.Invoke(new PreviewKeyboardEventArgs(
                PreviewKeyboardKind.Reset, 0));
            handled = true;
            return 0;
        }
        switch (message)
        {
            case WmMouseWheel when IsResizeWheelModifierPressed():
                ScaleWithWheel(unchecked((short)(wParam.ToInt64() >> 16)), lParam);
                handled = true;
                return 0;
            case WmMouseMove when IsPointerInputActive:
                lParam = DrainQueuedMouseMoves(hwnd, lParam);
                DispatchPointer(PreviewPointerKind.Move, lParam, 0, 0);
                handled = true;
                return 0;
            case WmLeftButtonDown when IsPointerInputActive:
                _capturedMouseButtons |= 1;
                _ = SetCapture(hwnd);
                DispatchPointer(PreviewPointerKind.ButtonDown, lParam, 1, 0);
                handled = true;
                return 0;
            case WmLeftButtonUp when IsPointerInputActive:
                _capturedMouseButtons = (byte)(_capturedMouseButtons & ~1);
                DispatchPointer(PreviewPointerKind.ButtonUp, lParam, 1, 0);
                if (_capturedMouseButtons == 0) _ = ReleaseCapture();
                handled = true;
                return 0;
            case WmRightButtonDown when IsRightButtonForwardingEnabled:
                if (IsPointerInputActive)
                {
                    _capturedMouseButtons |= 2;
                    _ = SetCapture(hwnd);
                    DispatchPointer(PreviewPointerKind.ButtonDown, lParam, 2, 0);
                }
                handled = true;
                return 0;
            case WmRightButtonUp when IsRightButtonForwardingEnabled || (_capturedMouseButtons & 2) != 0:
                if (IsPointerInputActive)
                    DispatchPointer(PreviewPointerKind.ButtonUp, lParam, 2, 0);
                if ((_capturedMouseButtons & 2) != 0)
                {
                    _capturedMouseButtons = (byte)(_capturedMouseButtons & ~2);
                    if (_capturedMouseButtons == 0) _ = ReleaseCapture();
                }
                handled = true;
                return 0;
            case WmMiddleButtonDown when IsPointerInputActive:
                _capturedMouseButtons |= 4;
                _ = SetCapture(hwnd);
                DispatchPointer(PreviewPointerKind.ButtonDown, lParam, 4, 0);
                handled = true;
                return 0;
            case WmMiddleButtonUp when IsPointerInputActive:
                _capturedMouseButtons = (byte)(_capturedMouseButtons & ~4);
                DispatchPointer(PreviewPointerKind.ButtonUp, lParam, 4, 0);
                if (_capturedMouseButtons == 0) _ = ReleaseCapture();
                handled = true;
                return 0;
            case WmMouseWheel when IsPointerInputActive:
                DispatchPointer(PreviewPointerKind.Wheel, lParam, 0,
                    unchecked((short)(wParam.ToInt64() >> 16)));
                handled = true;
                return 0;
            case WmSetCursor when (lParam.ToInt64() & 0xFFFF) == PreviewFrameHitTest.HtHomeButton:
                NativeCursor.SetHand();
                ShowSystemCursor();
                handled = true;
                return 1;
            case WmNcLeftButtonDown when wParam.ToInt32() == PreviewFrameHitTest.HtHomeButton:
            case WmNcLeftButtonDoubleClick when wParam.ToInt32() == PreviewFrameHitTest.HtHomeButton:
                // A second quick press arrives as a double click; both press Home.
                handled = true;
                if (HomeRequested is { } home)
                    _source.Dispatcher.BeginInvoke(DispatcherPriority.Input, home);
                Log("independent_window_home", ("mode", WindowMode));
                return 0;
            case WmNcMouseMove:
                SetHomeHover(wParam.ToInt32() == PreviewFrameHitTest.HtHomeButton);
                if (_homeHover) TrackNonClientLeave();
                break;
            case WmNcMouseLeave:
                SetHomeHover(false);
                break;
            case WmSetCursor when IsUsbControlEnabledForWindow:
                // USB touch control never captures the Windows pointer. A
                // previous reverse-control route may have left the process
                // cursor hidden or retained a resize/hand shape. Reassert
                // both the arrow shape and visibility for this HWND.
                NativeCursor.SetArrow();
                ShowSystemCursor();
                handled = true;
                return 1;
            case WmSetCursor when IsReverseControlActive:
                HideSystemCursor();
                handled = true;
                return 1;
            case WmKeyDown when IsPointerInputActive:
                _keyboardInput?.Invoke(new PreviewKeyboardEventArgs(
                    PreviewKeyboardKind.Down, wParam.ToInt32(),
                    (int)((lParam.ToInt64() >> 16) & 0x1FF)));
                handled = true;
                return 0;
            case WmSysKeyDown when IsPointerInputActive:
                _keyboardInput?.Invoke(new PreviewKeyboardEventArgs(
                    PreviewKeyboardKind.Down, wParam.ToInt32(),
                    (int)((lParam.ToInt64() >> 16) & 0x1FF)));
                handled = true;
                return 0;
            case WmKeyUp when IsPointerInputActive:
            case WmSysKeyUp when IsPointerInputActive:
                _keyboardInput?.Invoke(new PreviewKeyboardEventArgs(
                    PreviewKeyboardKind.Up, wParam.ToInt32(),
                    (int)((lParam.ToInt64() >> 16) & 0x1FF)));
                handled = true;
                return 0;
            case WmContextMenu when IsRightButtonForwardingEnabled:
            case WmNcRightButtonDown when IsRightButtonForwardingEnabled:
            case WmNcRightButtonUp when IsRightButtonForwardingEnabled:
                handled = true;
                return 0;
            case WmNcCalcSize when _managedContent is null:
                handled = true;
                return 0;
            case WmNcHitTest when _managedContent is null:
                handled = true;
                return _isFullScreen ? HtClient : HitTestWindow(lParam);
            case WmContextMenu:
            case WmRightButtonUp:
                handled = true;
                ShowContextMenu();
                return 0;
            case WmNcRightButtonDown when _managedContent is null:
            case WmNcRightButtonUp when _managedContent is null:
            case WmRightButtonDown when _managedContent is null:
                handled = true;
                ShowContextMenu();
                return 0;
            case WmNcLeftButtonDoubleClick when _managedContent is null &&
                wParam.ToInt32() == HtCaption:
                handled = true;
                _source.Dispatcher.BeginInvoke(DispatcherPriority.Input, ToggleFullScreen);
                return 0;
            case WmLeftButtonDoubleClick:
                handled = true;
                _source.Dispatcher.BeginInvoke(DispatcherPriority.Input, ToggleFullScreen);
                return 0;
            case WmEraseBackground:
                // The native DirectComposition visual paints the complete
                // client. Suppressing class-background erase avoids a white
                // flash while its swap chain is being resized/recreated.
                handled = true;
                return 1;
            case WmClose:
                handled = true;
                QueueClose();
                return 0;
            case WmSysKeyDown when wParam.ToInt32() == VkReturn && GetKeyState(VkMenu) < 0:
                handled = true;
                ToggleFullScreen();
                return 0;
            case WmDpiChanged:
                _source.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    // The frame thickness is specified in DIPs.
                    ApplyFrame(reflow: false);
                    _aspectController.Reflow();
                });
                break;
        }
        return 0;
    }

    private nint HitTestWindow(nint packedScreenPoint)
    {
        if (_handle == 0 || !GetWindowRect(_handle, out var bounds)) return HtClient;
        var packed = packedScreenPoint.ToInt64();
        var x = (short)(packed & 0xFFFF);
        var y = (short)((packed >> 16) & 0xFFFF);
        var dpi = GetDpiForWindow(_handle);
        var border = Math.Max(6, (int)Math.Round(8.0 * (dpi == 0 ? 1.0 : dpi / 96.0)));
        var home = _frameInsets.IsEmpty ? default : _frameSettings.Layout(dpi,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top).HomeHitArea;
        // A locked window neither moves nor resizes, but Home stays usable.
        if (_isFixed)
            return home.Contains(x - bounds.Left, y - bounds.Top)
                ? PreviewFrameHitTest.HtHomeButton : HtClient;
        // While touch control is on, the image belongs to the phone; the
        // frame still moves and resizes the window.
        return PreviewFrameHitTest.Classify(x, y, bounds.Left, bounds.Top,
            bounds.Right, bounds.Bottom, border, _frameInsets, home,
            contentIsClient: IsPointerInputEnabledForWindow);
    }

    private void QueueClose()
    {
        if (_disposed || _closeQueued) return;
        _closeQueued = true;
        Log("independent_window_close_queued", ("mode", WindowMode));
        try
        {
            _source.Dispatcher.BeginInvoke(DispatcherPriority.Send, Dispose);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher can enter shutdown between WM_CLOSE and the
            // queued callback. Dispose synchronously while the HWND is still
            // on this thread instead of surfacing an unhandled hook error.
            try { Dispose(); }
            catch (Exception error)
            {
                Log("independent_window_close_failed", ("error", AppLog.Error(error)));
            }
        }
    }

    private void ShowContextMenu()
    {
        if (_disposed) return;
        UpdateContextMenuLabels();
        _contextMenu.IsOpen = true;
        Log("independent_window_context_menu",
            ("mode", WindowMode), ("full_screen", _isFullScreen),
            ("top_most", _isTopMost), ("fixed", _isFixed));
    }

    private void UpdateContextMenuLabels()
    {
        _fullScreenItem.Header = LocalizationService.Get(
            _isFullScreen ? "IndependentWindowExitFullScreen" :
                "IndependentWindowEnterFullScreen");
        _windowMenuItem.Header = LocalizationService.Get("IndependentWindowWindowMenu");
        _styleItem.Header = LocalizationService.Get("IndependentWindowStyle");
        _displayMenuItem.Header = LocalizationService.Get("IndependentWindowDisplayMenu");
        _topMostItem.Header = LocalizationService.Get(
            _isTopMost ? "IndependentWindowUnpin" : "IndependentWindowPin");
        _fixedItem.Header = LocalizationService.Get(
            _isFixed ? "IndependentWindowUnfix" : "IndependentWindowFix");
        _fixedItem.IsEnabled = !_isFullScreen;
        if (_frameItem is not null)
        {
            _frameItem.Header = LocalizationService.Get(
                PreviewFrameService.Current.Enabled ? "IndependentWindowHideFrame" :
                    "IndependentWindowShowFrame");
            _frameItem.IsEnabled = !_isFullScreen;
        }
        if (_previewSettingsItem is not null)
            _previewSettingsItem.Header = LocalizationService.Get("PreviewSettingsMenu");
        if (_reverseControlMenuItem is not null)
        {
            _reverseControlMenuItem.Header = LocalizationService.Get(
                "IndependentWindowReverseControl");
            _reverseControlMenuItem.IsEnabled = !_isFullScreen;
        }
        if (_bluetoothControlItem is not null)
        {
            _bluetoothControlItem.Header = LocalizationService.Get(
                "IndependentWindowBluetoothControl");
            _bluetoothControlItem.IsEnabled = !_isFullScreen;
        }
        if (_usbControlItem is not null)
        {
            _usbControlItem.Header = LocalizationService.Get(
                "IndependentWindowWiredProjection");
            _usbControlItem.IsEnabled = !_isFullScreen;
        }
        if (_wirelessControlItem is not null)
        {
            _wirelessControlItem.Header = LocalizationService.Get(
                "IndependentWindowWirelessProjection");
            _wirelessControlItem.IsEnabled = !_isFullScreen;
        }
        if (_cornerItem is not null)
            _cornerItem.Header = LocalizationService.Get(
                _cornersEnabled ? "IndependentWindowRemoveCorners" :
                    "IndependentWindowKeepCorners");
        if (_imageSettingsItem is not null)
            _imageSettingsItem.Header = LocalizationService.Get(
                "IndependentWindowImageSettings");
        if (_projectionSettingsItem is not null)
            _projectionSettingsItem.Header = LocalizationService.Get(
                "IndependentWindowProjectionSettings");
        _rotateLeftItem.Header = LocalizationService.Get("IndependentWindowRotateLeft");
        _rotateRightItem.Header = LocalizationService.Get("IndependentWindowRotateRight");
        _muteMenuItem.Header = LocalizationService.Get("IndependentWindowAudioMenu");
        _muteThisItem.Header = LocalizationService.Get(
            _isAudioEnabled?.Invoke() == false
                ? "IndependentWindowUnmuteThis"
                : "IndependentWindowMuteThis");
        _muteOthersItem.Header = LocalizationService.Get("IndependentWindowMuteOthers");
        _muteOthersItem.Visibility = IndependentWindowAudioPolicy.ShowMuteOthers(
            _connectedDeviceCount?.Invoke() ?? 1)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _closeItem.Header = LocalizationService.Get("IndependentWindowClose");
    }

    private void ToggleWindowAudio()
    {
        if (_disposed || _isAudioEnabled is null || _setAudioEnabled is null) return;
        var enabled = !_isAudioEnabled();
        _setAudioEnabled(enabled);
        UpdateContextMenuLabels();
        Log("independent_window_audio",
            ("mode", WindowMode), ("enabled", enabled));
    }

    private void MuteOtherWindows()
    {
        if (_disposed || _muteOtherWindows is null) return;
        _muteOtherWindows();
        UpdateContextMenuLabels();
        Log("independent_window_mute_others", ("mode", WindowMode));
    }

    private void ShowImageSettings()
    {
        if (_disposed || _showImageSettings is null) return;
        _showImageSettings(_handle);
        Log("independent_window_image_settings", ("mode", WindowMode));
    }

    private void ShowStyleSettings()
    {
        if (_disposed || _handle == 0) return;
        if (_styleWindow is not null)
        {
            if (_styleWindow.WindowState == WindowState.Minimized)
                _styleWindow.WindowState = WindowState.Normal;
            _styleWindow.Activate();
            return;
        }
        var window = new WindowStyleSettingsWindow(_windowOpacity, SetWindowOpacity,
            _opaqueOnHover, SetOpaqueOnHover);
        _ = new WindowInteropHelper(window) { Owner = _handle };
        _styleWindow = window;
        window.Closed += (_, _) => _styleWindow = null;
        window.Show();
    }

    private bool SetWindowOpacity(double opacity)
    {
        if (_disposed || _handle == 0 || !double.IsFinite(opacity)) return false;
        opacity = Math.Clamp(opacity, 0.1, 1.0);
        if (!ApplyWindowOpacity(_opaqueOnHover && _isPointerOverWindow ? 1.0 : opacity)) return false;
        _windowOpacity = opacity;
        return true;
    }

    private double EffectiveWindowOpacity => _opaqueOnHover && _isPointerOverWindow ? 1.0 : _windowOpacity;

    private bool SetOpaqueOnHover(bool enabled)
    {
        if (_disposed) return false;
        var wasEnabled = _opaqueOnHover;
        var wasHovered = _isPointerOverWindow;
        _opaqueOnHover = enabled;
        _isPointerOverWindow = enabled && IsPointerOverPreview();
        if (!ApplyWindowOpacity(EffectiveWindowOpacity))
        {
            _opaqueOnHover = wasEnabled;
            _isPointerOverWindow = wasHovered;
            return false;
        }
        if (enabled) _hoverOpacityTimer.Start();
        else _hoverOpacityTimer.Stop();
        return true;
    }

    private bool IsPointerOverPreview()
    {
        if (_disposed || _handle == 0 || !IsWindowVisible(_handle) || IsIconic(_handle) ||
            !GetCursorPos(out var point)) return false;
        var hovered = WindowFromPoint(point);
        return hovered == _handle || GetAncestor(hovered, 2) == _handle;
    }

    private void UpdateHoverState(bool hovered)
    {
        if (_disposed || !_opaqueOnHover) return;
        _isPointerOverWindow = hovered;
        _ = ApplyWindowOpacity(EffectiveWindowOpacity);
    }

    private bool ApplyWindowOpacity(double opacity, bool force = false)
    {
        if (_disposed || _handle == 0) return false;
        if (!force && _appliedOpacity == opacity) return true;
        if (_managedContent is null)
        {
            // WS_EX_LAYERED cannot fade a no-redirection swap chain. Apply
            // opacity to its native composition visual, leaving capture alone.
            if (!NativeCore.SetDeviceWindowOpacity(_sessionHandle, _handle, opacity)) return false;
        }
        else
        {
            var previousOpacity = _appliedOpacity;
            _appliedOpacity = opacity;
            var style = GetWindowLongPtrW(_handle, GwlExStyle);
            _ = SetWindowLongPtrW(_handle, GwlExStyle, opacity < 1.0
                ? (nint)(style.ToInt64() | WsExLayered)
                : (nint)(style.ToInt64() & ~WsExLayered));
            if (opacity < 1.0 && !SetLayeredWindowAttributes(_handle, 0, (byte)Math.Round(opacity * 255), LwaAlpha))
            {
                _appliedOpacity = previousOpacity;
                _ = SetWindowLongPtrW(_handle, GwlExStyle, style);
                return false;
            }
        }
        _appliedOpacity = opacity;
        if (_protectedOverlay is not null) _protectedOverlay.Opacity = opacity;
        return true;
    }

    private void ShowProjectionSettings()
    {
        if (_disposed || _showProjectionSettings is null) return;
        _showProjectionSettings();
        Log("independent_window_projection_settings", ("mode", WindowMode));
    }

    private void ToggleTopMost()
    {
        if (_disposed || _handle == 0) return;
        _isTopMost = !_isTopMost;
        _ = SetWindowPos(_handle, _isTopMost ? HwndTopMost : HwndNoTopMost,
            0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        UpdateContextMenuLabels();
        PublishPlacement();
        Log("independent_window_topmost",
            ("mode", WindowMode), ("enabled", _isTopMost));
    }

    private void ToggleFixedWindow()
    {
        if (_disposed || _handle == 0 || _isFullScreen) return;
        _isFixed = !_isFixed;
        var style = GetWindowLongPtrW(_handle, GwlStyle).ToInt64();
        style = _isFixed ? style & ~WsThickFrame : style | WsThickFrame;
        _ = SetWindowLongPtrW(_handle, GwlStyle, (nint)style);
        _ = SetWindowPos(_handle, 0, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        if (!_isFixed) _aspectController.Reflow();
        UpdateContextMenuLabels();
        Log("independent_window_fixed",
            ("mode", WindowMode), ("enabled", _isFixed));
    }

    private readonly Func<nint> _inputForegroundWindow = GetForegroundWindow;
    private bool IsReverseControlActive => !IsVisibilityHidden && _pointerInput is not null &&
        (_isReverseControlEnabled?.Invoke(_handle) ?? false) &&
        _inputForegroundWindow() == _handle;
    private bool IsReverseControlEnabledForWindow => !IsVisibilityHidden && _pointerInput is not null &&
        (_isReverseControlEnabled?.Invoke(_handle) ?? false);
    private bool IsUsbControlEnabledForWindow => !IsVisibilityHidden && _pointerInput is not null &&
        (_isUsbControlEnabled?.Invoke() ?? false);
    // The reverse-control callback also includes wired/wireless touch targets.
    // Reserve their right-click for the window menu, even when a phone shortcut
    // (including the default Home binding) uses it. Only Bluetooth forwards it.
    private bool IsRightButtonForwardingEnabled =>
        IsReverseControlEnabledForWindow && !IsUsbControlEnabledForWindow;
    private bool IsPointerInputEnabledForWindow =>
        IsReverseControlEnabledForWindow || IsUsbControlEnabledForWindow;
    private bool IsPointerInputActive =>
        IsReverseControlActive || (IsUsbControlEnabledForWindow &&
            _inputForegroundWindow() == _handle);

    private void EnableReverseControl()
    {
        if (_disposed || _isFullScreen) return;
        EnsureFixedWindow();
        _requestReverseControl?.Invoke(_handle);
        UpdateContextMenuLabels();
    }


    private static bool IsKeyDown(int virtualKey) => GetKeyState(virtualKey) < 0;

    // Alt+wheel resizes the window; Ctrl+wheel stays the phone's pinch zoom.
    // The keyboard hook can swallow Alt while typing goes to the phone, so the
    // owner's own modifier state counts too.
    private bool IsResizeWheelModifierPressed() =>
        !_isFullScreen && !_isFixed &&
        !IsKeyDown(VkControl) && (IsKeyDown(VkMenu) || ResizeModifierPressed?.Invoke() == true);

    private void ScaleWithWheel(short delta, nint lParam)
    {
        if (delta == 0) return;
        // WM_MOUSEWHEEL carries the pointer in screen coordinates.
        var x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var factor = Math.Pow(WheelScaleStep, delta / 120.0);
        if (_aspectController.ScaleAround(factor, x, y)) PublishPlacement();
    }

    private void HideSystemCursor()
    {
        // ShowCursor uses one process-wide display counter. Other native
        // windows or overlays can increment it after control is enabled, so
        // assert hidden state whenever this independent HWND owns the cursor.
        var cursor = new CursorInfo
        {
            Size = (uint)Marshal.SizeOf<CursorInfo>(),
        };
        if (!GetCursorInfo(ref cursor))
        {
            Log("independent_cursor_query_failed",
                ("win32_error", Marshal.GetLastWin32Error()));
            // The caller still consumes WM_SETCURSOR. Do not change
            // ShowCursor's display count when it cannot be queried: repeated
            // cursor messages would otherwise leave it hidden after reverse
            // control has been turned off.
            return;
        }
        if ((cursor.Flags & CursorShowing) != 0)
            while (ShowCursor(false) >= 0) { }
    }

    private void ShowSystemCursor()
    {
        var cursor = new CursorInfo
        {
            Size = (uint)Marshal.SizeOf<CursorInfo>(),
        };
        if (!GetCursorInfo(ref cursor))
        {
            Log("independent_cursor_query_failed",
                ("win32_error", Marshal.GetLastWin32Error()));
            return;
        }
        if ((cursor.Flags & CursorShowing) == 0)
            while (ShowCursor(true) < 0) { }
    }

    private void EnsureFixedWindow()
    {
        if (_isFixed) return;
        ToggleFixedWindow();
    }

    internal void PrepareForUsbControl()
    {
        if (_disposed || _handle == 0) return;
        // With a frame, clicks on the image reach the phone while the frame
        // keeps moving/resizing the window, so locking it is unnecessary.
        if (!_isFixed && _frameInsets.IsEmpty) ToggleFixedWindow();
        if (!_isTopMost) ToggleTopMost();
        _ = SetForegroundWindow(_handle);
        UpdateContextMenuLabels();
    }

    private void DispatchPointer(PreviewPointerKind kind, nint lParam,
        byte button, int wheel)
    {
        if (_pointerInput is null || !GetClientRect(_handle, out var rect)) return;
        var packed = lParam.ToInt64();
        var x = unchecked((short)(packed & 0xFFFF));
        var y = unchecked((short)((packed >> 16) & 0xFFFF));
        var sourceWidth = (_rotation & 1) == 0 ? _sourceWidth : _sourceHeight;
        var sourceHeight = (_rotation & 1) == 0 ? _sourceHeight : _sourceWidth;
        // Report coordinates relative to the video area inside the frame.
        _pointerInput(new PreviewPointerEventArgs(kind,
            unchecked((short)(x - _frameInsets.Left)), unchecked((short)(y - _frameInsets.Top)),
            button, wheel,
            Math.Max(1, rect.Right - rect.Left - _frameInsets.Horizontal),
            Math.Max(1, rect.Bottom - rect.Top - _frameInsets.Vertical),
            sourceWidth, sourceHeight, _rotation));
    }

    internal PreviewFrameInsets FrameInsets => _frameInsets;

    private void SetHomeHover(bool hover)
    {
        if (_homeHover == hover) return;
        _homeHover = hover;
        ApplyFrame(reflow: false);
    }

    private void TrackNonClientLeave()
    {
        var track = new TrackMouseEventData
        {
            Size = (uint)Marshal.SizeOf<TrackMouseEventData>(),
            Flags = TmeLeave | TmeNonClient,
            Window = _handle,
        };
        _ = TrackMouseEvent(ref track);
    }

    private void OnPreviewFrameChanged()
    {
        if (_disposed) return;
        if (!_source.Dispatcher.CheckAccess())
        {
            _source.Dispatcher.BeginInvoke(DispatcherPriority.Normal, OnPreviewFrameChanged);
            return;
        }
        ApplyFrame();
        UpdateContextMenuLabels();
    }

    private void ApplyFrame(bool reflow = true)
    {
        if (_disposed || _handle == 0 || _managedContent is not null) return;
        var settings = PreviewFrameService.Current;
        _frameSettings = settings;
        _ = GetClientRect(_handle, out var client);
        var layout = _isFullScreen ? PreviewFrameLayout.None : settings.Layout(
            GetDpiForWindow(_handle), client.Right - client.Left, client.Bottom - client.Top);
        var frame = new NativeCore.NativeWindowFrame
        {
            Left = layout.Insets.Left,
            Top = layout.Insets.Top,
            Right = layout.Insets.Right,
            Bottom = layout.Insets.Bottom,
            Argb = settings.ToArgb(((uint)ThemeService.Accent.R << 16) |
                ((uint)ThemeService.Accent.G << 8) | ThemeService.Accent.B),
            // Outer corners stay concentric with the iPhone's rounded corners.
            Roundness = 0,
            Flags = (settings.FollowsImageEdge ? NativeCore.NativeWindowFrame.AutoColorFlag : 0) |
                (_homeHover ? NativeCore.NativeWindowFrame.ButtonHoverFlag : 0),
        };
        if (!layout.HomePill.IsEmpty)
        {
            frame.ButtonAlign = settings.HomeButtonPosition switch
            {
                PreviewHomeButtonPosition.Left => NativeCore.NativeWindowFrame.AlignLeft,
                PreviewHomeButtonPosition.Right => NativeCore.NativeWindowFrame.AlignRight,
                _ => NativeCore.NativeWindowFrame.AlignCenter,
            };
            // Same anchoring as PreviewFrameSettings.Layout, so the pill follows
            // live resizing without a round trip.
            frame.ButtonOffset = layout.HomeHitArea.Width / 2;
            frame.ButtonWidth = layout.HomePill.Width;
            frame.ButtonHeight = layout.HomePill.Height;
            frame.ButtonCenterY = layout.Insets.Bottom / 2;
        }
        // Only offset input/aspect when the renderer actually draws the
        // outline (an older native core lacks the export).
        string? frameError = "preview_not_attached";
        var applied = _sessionHandle != 0 && _attached &&
            NativeCore.SetDeviceWindowFrame(_sessionHandle, _handle, frame, out frameError)
            ? layout.Insets : default;
        if (!layout.Insets.IsEmpty && applied.IsEmpty && frameError != _lastFrameError)
        {
            // Without the native outline the frame stays hidden; record why once.
            Log("independent_window_frame_failed",
                ("mode", WindowMode), ("error", frameError ?? "unknown"));
        }
        _lastFrameError = applied.IsEmpty ? frameError : null;
        if (applied == _frameInsets) return;
        _frameInsets = applied;
        _pointerInput?.Invoke(new PreviewPointerEventArgs(PreviewPointerKind.Reset, 0, 0, 0, 0));
        _protectedOverlay?.UpdateBounds();
        if (reflow) _aspectController.Reflow();
        Log("independent_window_frame",
            ("mode", WindowMode), ("insets", $"{applied.Left},{applied.Top},{applied.Right},{applied.Bottom}"),
            ("enabled", settings.Enabled), ("home", settings.HomeButton));
    }

    private static nint DrainQueuedMouseMoves(nint hwnd, nint currentLParam)
    {
        var latest = currentLParam;
        var queued = new NativeMessage();
        var drained = 0;
        while (drained++ < 64 && PeekMessageW(ref queued, hwnd, WmMouseMove, WmMouseMove, PmRemove))
            latest = queued.LParam;
        return latest;
    }

    private void ToggleCorners()
    {
        if (_managedContent is not null || _sessionHandle == 0 || _handle == 0) return;
        _cornersEnabled = !_cornersEnabled;
        _ = NativeCore.SetDeviceWindowCornerProfile(_sessionHandle, _handle,
            _cornersEnabled ? _cornerRadius : 0, _cornerExponent);
        UpdateContextMenuLabels();
        Log("independent_window_corners",
            ("mode", WindowMode), ("enabled", _cornersEnabled));
    }

    private void Rotate(int delta)
    {
        if (_handle == 0) return;
        _pointerInput?.Invoke(new PreviewPointerEventArgs(PreviewPointerKind.Reset, 0, 0, 0, 0));
        _rotation = ((_rotation + delta) % 4 + 4) % 4;
        if (_managedContent is not null)
            _managedContent.LayoutTransform = new RotateTransform(_rotation * 90);
        else if (_sessionHandle != 0)
            _ = NativeCore.SetDeviceWindowRotation(_sessionHandle, _handle, _rotation);
        ApplyRotatedDimensions();
        Log("independent_window_rotation",
            ("mode", WindowMode), ("quarter_turns", _rotation));
    }

    private void ApplyRotatedDimensions()
    {
        if (_sourceWidth == 0 || _sourceHeight == 0) return;
        if ((_rotation & 1) != 0)
            _aspectController.SetSourceDimensions(_sourceHeight, _sourceWidth);
        else
            _aspectController.SetSourceDimensions(_sourceWidth, _sourceHeight);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed || _handle == 0) return;
        if (_managedContent is null)
        {
            // The frame can follow the accent colour.
            OnPreviewFrameChanged();
            return;
        }
        var dark = ThemeService.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(_handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    public void Dispose()
    {
        if (_disposed) return;
        PublishPlacement();
        Log("independent_window_dispose_begin",
            ("mode", WindowMode), ("attached", _attached),
            ("full_screen", _isFullScreen));
        _disposed = true;
        _hoverOpacityTimer.Stop();
        OpenWindows.Remove(this);
        VisibilityChanged?.Invoke();
        ThemeService.ThemeChanged -= OnThemeChanged;
        PreviewFrameService.Changed -= OnPreviewFrameChanged;
        _styleWindow?.Close();
        _styleWindow = null;
        CloseControlsOverlay();
        _protectedOverlay?.Close();
        _protectedOverlay = null;
        _contextMenu.IsOpen = false;
        _contextMenu.PlacementTarget = null;
        _ = ShowWindow(_handle, SwHide);
        _aspectController.Dispose();
        if (_attached && _handle != 0)
        {
            // Isolate native detach failures so subsequent HWND/GDI cleanup still runs.
            try { _detachPreview(_handle); }
            catch (Exception ex) { Log("detach_preview_failed", ("error", ex.Message)); }
            _attached = false;
        }
        if (_managedContentRoot is not null)
        {
            if (_managedContent is not null)
                _managedContent.LayoutTransform =
                    _managedContentOriginalLayoutTransform ?? Transform.Identity;
            _managedContentRoot.Child = null;
            _source.RootVisual = null;
        }
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _handle = 0;
        if (_managedContentRoot is not null)
        {
            try { _managedContentDetached?.Invoke(); }
            catch { /* Window teardown remains best-effort. */ }
        }
        if (_largeIcon != 0) _ = DestroyIcon(_largeIcon);
        if (_smallIcon != 0 && _smallIcon != _largeIcon) _ = DestroyIcon(_smallIcon);
        _largeIcon = 0;
        _smallIcon = 0;
        Log("independent_window_disposed", ("mode", WindowMode));
        var closedHandlers = Closed;
        Closed = null;
        if (closedHandlers is null) return;
        foreach (EventHandler handler in closedHandlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception error)
            {
                Log("independent_window_closed_handler_failed",
                    ("mode", WindowMode), ("error", AppLog.Error(error)));
            }
        }
    }

    private static string[] ReadDroppedFiles(nint drop)
    {
        try
        {
            var count = DragQueryFileW(drop, uint.MaxValue, null, 0);
            var files = new List<string>((int)count);
            for (uint index = 0; index < count; index++)
            {
                var length = DragQueryFileW(drop, index, null, 0);
                var buffer = new char[length + 1];
                if (DragQueryFileW(drop, index, buffer, (uint)buffer.Length) > 0)
                    files.Add(new string(buffer, 0, (int)length));
            }
            return files.ToArray();
        }
        finally { DragFinish(drop); }
    }

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(nint drop, uint index, char[]? file, uint length);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(nint drop);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint filterInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventData
    {
        internal uint Size;
        internal uint Flags;
        internal nint Window;
        internal uint HoverTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TrackMouseEventData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct HoverPoint { public int X, Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out HoverPoint point);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(HoverPoint point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint Cursor;
        internal int X;
        internal int Y;
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
    private struct NativeMessage
    {
        internal nint Hwnd;
        internal uint Message;
        internal nint WParam;
        internal nint LParam;
        internal uint Time;
        internal int PointX;
        internal int PointY;
        internal uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint window, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(ref NativeMessage message, nint window,
        uint minMessage, uint maxMessage, uint removeMessage);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPropW(nint window, string name, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint RemovePropW(nint window, string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int iconIndex,
        out nint largeIcon, out nint smallIcon, uint iconCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out WindowRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute,
        ref int value, int valueSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttributeColor(nint window, int attribute,
        ref uint value, int valueSize);

}
