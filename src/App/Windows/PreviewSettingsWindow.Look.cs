using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.Windows;

// The two look tiles (borderless with rounded corners, or a slim iPhone case
// in a chosen color) and the switches for the controls drawn over previews.
// The case is the preview frame with a fixed case colour; the detailed frame
// options below the tiles stay available for anything in between.
public partial class PreviewSettingsWindow
{
    private const double CaseThicknessDips = 10;
    private static readonly (string Key, string Hex)[] CaseColorChoices =
    [
        ("PreviewCaseBlack", "#1C1C1F"), ("PreviewCaseSilver", "#D9D9DE"),
        ("PreviewCaseTitanium", "#8E8A83"), ("PreviewCaseBlue", "#3D4E68"),
    ];

    private void BuildCaseColors()
    {
        foreach (var (key, hex) in CaseColorChoices)
        {
            var swatch = new Button
            {
                Width = 30, Height = 30, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0), Tag = hex, Cursor = System.Windows.Input.Cursors.Hand,
                Template = SwatchTemplate(),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            swatch.SetResourceReference(ToolTipProperty, key);
            System.Windows.Automation.AutomationProperties.SetName(swatch, Localization.LocalizationService.Get(key));
            swatch.Click += (_, _) => ApplyCase(hex);
            CaseColors.Children.Add(swatch);
        }
    }

    private static bool IsCaseColor(string color) =>
        CaseColorChoices.Any(choice => string.Equals(choice.Hex, color, StringComparison.OrdinalIgnoreCase));

    private void SyncLook(PreviewFrameSettings settings)
    {
        var isCase = settings.Enabled && !settings.UseAccentColor && !settings.AutoColor && IsCaseColor(settings.Color);
        MarkTile(BorderlessTile, !settings.Enabled);
        MarkTile(CaseTile, isCase);
        CaseColors.Visibility = isCase ? Visibility.Visible : Visibility.Collapsed;
        if (isCase) CaseTileSample.Background =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Color));
        foreach (var swatch in CaseColors.Children.OfType<Button>())
        {
            var selected = isCase && string.Equals(swatch.Tag as string, settings.Color, StringComparison.OrdinalIgnoreCase);
            swatch.BorderThickness = new Thickness(selected ? 3 : 1);
            swatch.SetResourceReference(BorderBrushProperty, selected ? "AccentBrush" : "BorderSoftBrush");
        }
        var everyday = (Application.Current as App)?.UpdateSettings.Everyday ?? new();
        HoverOnlyCheckBox.IsChecked = everyday.PreviewControlsOnHoverOnly;
        SnapCheckBox.IsChecked = everyday.SnapPreviewToEdges;
        BeyondScreenCheckBox.IsChecked = everyday.PreviewBeyondScreen;
        SyncDots(everyday);
    }

    private static void MarkTile(Button tile, bool selected)
    {
        tile.BorderThickness = new Thickness(selected ? 2 : 1);
        tile.SetResourceReference(BorderBrushProperty, selected ? "AccentBrush" : "BorderSoftBrush");
    }

    private void OnBorderlessTileClick(object sender, RoutedEventArgs e) => Update(s => s.Enabled = false);

    private void OnCaseTileClick(object sender, RoutedEventArgs e)
    {
        var current = PreviewFrameService.Current.Color;
        ApplyCase(IsCaseColor(current) ? current : CaseColorChoices[0].Hex);
    }

    private void ApplyCase(string hex) => Update(s =>
    {
        s.Enabled = true;
        s.UseAccentColor = false;
        s.AutoColor = false;
        s.Color = hex;
        s.Opacity = 1.0;
        s.ThicknessDips = CaseThicknessDips;
    });

    private void OnEverydayCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsInitialized || Application.Current is not App app) return;
        var everyday = app.UpdateSettings.Everyday;
        everyday.PreviewControlsOnHoverOnly = HoverOnlyCheckBox.IsChecked == true;
        everyday.SnapPreviewToEdges = SnapCheckBox.IsChecked == true;
        everyday.PreviewBeyondScreen = BeyondScreenCheckBox.IsChecked == true;
        app.SaveUpdateSettings();
    }
}
