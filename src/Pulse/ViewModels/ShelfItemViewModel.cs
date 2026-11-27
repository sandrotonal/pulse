using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Models;
using Pulse.Services.System;
using Windows.UI;

namespace Pulse.ViewModels;

/// <summary>
/// Presentation wrapper for a staged Drop Shelf item with optional decoded thumbnail and theme-aware styling.
/// </summary>
public sealed class ShelfItemViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush DarkCategoryBackground = new(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkCategoryBorder = new(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkCategoryForeground = new(Color.FromArgb(0xEE, 0xF3, 0xF4, 0xF6));
    private static readonly SolidColorBrush LightCategoryBackground = new(Color.FromArgb(0x0D, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightCategoryBorder = new(Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightCategoryForeground = new(Color.FromArgb(0xEE, 0x14, 0x15, 0x18));

    private static readonly SolidColorBrush DarkPinBackground = new(Color.FromArgb(0xE6, 0x1A, 0x1B, 0x20));
    private static readonly SolidColorBrush DarkPinStrokePinned = new(Color.FromArgb(0x66, 0xFF, 0xD6, 0x0A));
    private static readonly SolidColorBrush DarkPinStrokeIdle = new(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkPinFgPinned = new(Color.FromArgb(0xFF, 0xFF, 0xD6, 0x0A));
    private static readonly SolidColorBrush DarkPinFgIdle = new(Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6));

    private static readonly SolidColorBrush LightPinBackground = new(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush LightPinStrokePinned = new(Color.FromArgb(0x88, 0xC4, 0x7A, 0x00));
    private static readonly SolidColorBrush LightPinStrokeIdle = new(Color.FromArgb(0x24, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightPinFgPinned = new(Color.FromArgb(0xFF, 0xB8, 0x6E, 0x00));
    private static readonly SolidColorBrush LightPinFgIdle = new(Color.FromArgb(0xFF, 0x14, 0x15, 0x18));

    private ShelfItem _model;
    private bool _isLightTheme;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ShelfItemViewModel(ShelfItem model, bool isLightTheme = false)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _isLightTheme = isLightTheme;
        Thumbnail = ShelfShellService.TryCreateThumbnail(model);
    }

    public ShelfItem Model => _model;
    public string Id => _model.Id;
    public string DisplayName => _model.DisplayName;
    public string FilePath => _model.FilePath;
    public string ExtensionBadge => _model.ExtensionBadge;
    public string IconGlyph => _model.IconGlyph;
    public string SizeLabel => _model.SizeLabel;
    public string TooltipText => _model.TooltipText;
    public bool IsPinned => _model.IsPinned;
    public Visibility PinVisibility => _model.IsPinned ? Visibility.Visible : Visibility.Collapsed;
    public double PinButtonOpacity => _model.IsPinned ? 1.0 : 0.0;
    public string PinMenuLabel => _model.IsPinned ? "Sabitlemeyi Kaldır" : "Rafa Sabitle";
    public string PinToolTip => _model.IsPinned ? "Sabitlemeyi Kaldır" : "Rafa Sabitle";
    public string PinGlyph => _model.IsPinned ? "\uE840" : "\uE718";

    public SolidColorBrush PinBadgeBackgroundBrush => _isLightTheme ? LightPinBackground : DarkPinBackground;
    public SolidColorBrush PinBadgeStrokeBrush => _isLightTheme
        ? (_model.IsPinned ? LightPinStrokePinned : LightPinStrokeIdle)
        : (_model.IsPinned ? DarkPinStrokePinned : DarkPinStrokeIdle);
    public SolidColorBrush PinBadgeForegroundBrush => _isLightTheme
        ? (_model.IsPinned ? LightPinFgPinned : LightPinFgIdle)
        : (_model.IsPinned ? DarkPinFgPinned : DarkPinFgIdle);

    public bool IsImage => _model.Kind == ShelfItemKind.Image;
    public Visibility ImageActionsVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
    public string SubtitleLabel => _model.Kind == ShelfItemKind.Folder
        ? "Klasör"
        : string.IsNullOrWhiteSpace(_model.ExtensionBadge)
            ? _model.SizeLabel
            : $"{_model.ExtensionBadge} · {_model.SizeLabel}";
    public BitmapImage? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public Visibility ThumbnailVisibility => HasThumbnail ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => HasThumbnail ? Visibility.Collapsed : Visibility.Visible;

    public SolidColorBrush CategoryBackgroundBrush => _isLightTheme ? LightCategoryBackground : DarkCategoryBackground;
    public SolidColorBrush CategoryBorderBrush => _isLightTheme ? LightCategoryBorder : DarkCategoryBorder;
    public SolidColorBrush CategoryForegroundBrush => _isLightTheme ? LightCategoryForeground : DarkCategoryForeground;

    /// <summary>Updates mutable metadata (e.g., pin state) in-place without recreating the ViewModel or thumbnail.</summary>
    public void UpdateModel(ShelfItem updated)
    {
        if (updated is null)
        {
            return;
        }

        var pinChanged = _model.IsPinned != updated.IsPinned;
        _model = updated;
        if (pinChanged)
        {
            NotifyPinPropertiesChanged();
        }
    }

    public void RefreshTheme(bool isLightTheme)
    {
        if (_isLightTheme == isLightTheme)
        {
            return;
        }

        _isLightTheme = isLightTheme;
        NotifyPinPropertiesChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryBackgroundBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryBorderBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryForegroundBrush)));
    }

    private void NotifyPinPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinButtonOpacity)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinMenuLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinToolTip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinGlyph)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinBadgeBackgroundBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinBadgeStrokeBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinBadgeForegroundBrush)));
    }
}
