using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

// The main window supplies visibility and placement settings for the preview dots.
public partial class MainWindow : IPreviewOverlayActions
{
    private void InitializePreviewOverlay()
    {
        _secondaryMirrors.OverlayActions = this;
        _secondaryMirrors.ResizeModifierPressed = () =>
            (_keyboardRouter.PressedModifiers & KeyboardShortcut.Alt) != 0;
    }

    PreviewOverlayState IPreviewOverlayActions.GetOverlayState(string udid, nint window)
    {
        var everyday = EverydaySettingsForInput;
        return new(
            ControlsOnHoverOnly: everyday.PreviewControlsOnHoverOnly,
            MoveDot: everyday.PreviewMoveDot, MoveDotX: everyday.PreviewMoveDotX, MoveDotY: everyday.PreviewMoveDotY,
            ResizeDot: everyday.PreviewResizeDot, ResizeDotX: everyday.PreviewResizeDotX,
            ResizeDotY: everyday.PreviewResizeDotY);
    }
}
