using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Models;
using Pulse.Services.System;
using Windows.UI;

namespace Pulse.ViewModels;

/// <summary>
/// Presentation wrapper for a staged Drop Shelf item with optional decoded thumbnail and category styling.
/// </summary>
public sealed class ShelfItemViewModel
{
    public ShelfItemViewModel(ShelfItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Thumbnail = ShelfShellService.TryCreateThumbnail(model);

        var (bg, border, fg) = ResolveCategoryColors(model);
        CategoryBackgroundBrush = new SolidColorBrush(bg);
        CategoryBorderBrush = new SolidColorBrush(border);
        CategoryForegroundBrush = new SolidColorBrush(fg);
    }

    public ShelfItem Model { get; }
    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string FilePath => Model.FilePath;
    public string ExtensionBadge => Model.ExtensionBadge;
    public string IconGlyph => Model.IconGlyph;
    public string SizeLabel => Model.SizeLabel;
    public string TooltipText => Model.TooltipText;
    public bool IsPinned => Model.IsPinned;
    public Visibility PinVisibility => Model.IsPinned ? Visibility.Visible : Visibility.Collapsed;
    public string PinMenuLabel => Model.IsPinned ? "Sabitlemeyi Kaldır" : "Rafa Sabitle";
    public bool IsImage => Model.Kind == ShelfItemKind.Image;
    public Visibility ImageActionsVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
    public string SubtitleLabel => Model.Kind == ShelfItemKind.Folder
        ? "Klasör"
        : string.IsNullOrWhiteSpace(Model.ExtensionBadge)
            ? Model.SizeLabel
            : $"{Model.ExtensionBadge} · {Model.SizeLabel}";
    public BitmapImage? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public Visibility ThumbnailVisibility => HasThumbnail ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => HasThumbnail ? Visibility.Collapsed : Visibility.Visible;

    public SolidColorBrush CategoryBackgroundBrush { get; }
    public SolidColorBrush CategoryBorderBrush { get; }
    public SolidColorBrush CategoryForegroundBrush { get; }

    private static (Color Background, Color Border, Color Foreground) ResolveCategoryColors(ShelfItem item)
    {
        // Apple Liquid Glass monochrome hierarchy:
        // Pure neutral frosted glass with subtle specular border and high-legibility text.
        // Replaces oversaturated neon "AI slop" with authentic macOS/iOS obsidian glass.
        return (
            Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), // 7% translucent white
            Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), // 12% specular hairline
            Color.FromArgb(0xEE, 0xF3, 0xF4, 0xF6)  // Apple System Gray 6
        );
    }
}
