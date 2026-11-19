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
    public BitmapImage? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public Visibility ThumbnailVisibility => HasThumbnail ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => HasThumbnail ? Visibility.Collapsed : Visibility.Visible;

    public SolidColorBrush CategoryBackgroundBrush { get; }
    public SolidColorBrush CategoryBorderBrush { get; }
    public SolidColorBrush CategoryForegroundBrush { get; }

    private static (Color Background, Color Border, Color Foreground) ResolveCategoryColors(ShelfItem item)
    {
        if (item.Kind == ShelfItemKind.Folder)
        {
            return (Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B), Color.FromArgb(0x66, 0xF5, 0x9E, 0x0B), Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));
        }

        var ext = item.ExtensionBadge.ToUpperInvariant();
        return ext switch
        {
            "PNG" or "JPG" or "JPEG" or "WEBP" or "GIF" or "SVG" or "BMP" or "ICO" =>
                (Color.FromArgb(0x2E, 0xA8, 0x55, 0xF7), Color.FromArgb(0x55, 0xA8, 0x55, 0xF7), Color.FromArgb(0xFF, 0xD8, 0xB4, 0xFE)),
            "CS" or "TS" or "JS" or "PY" or "JSON" or "XAML" or "HTML" or "CSS" or "RS" or "GO" or "CPP" or "C" or "SQL" or "XML" or "YML" or "YAML" =>
                (Color.FromArgb(0x28, 0x3B, 0x82, 0xF6), Color.FromArgb(0x50, 0x3B, 0x82, 0xF6), Color.FromArgb(0xFF, 0x93, 0xC5, 0xFD)),
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" =>
                (Color.FromArgb(0x2E, 0xF5, 0x9E, 0x0B), Color.FromArgb(0x55, 0xF5, 0x9E, 0x0B), Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)),
            "PDF" or "DOC" or "DOCX" or "TXT" or "MD" or "RTF" or "LOG" =>
                (Color.FromArgb(0x2E, 0xEF, 0x44, 0x44), Color.FromArgb(0x55, 0xEF, 0x44, 0x44), Color.FromArgb(0xFF, 0xFC, 0xA5, 0xA5)),
            "XLS" or "XLSX" or "CSV" =>
                (Color.FromArgb(0x28, 0x10, 0xB9, 0x81), Color.FromArgb(0x50, 0x10, 0xB9, 0x81), Color.FromArgb(0xFF, 0x6E, 0xE7, 0xB7)),
            "MP3" or "WAV" or "FLAC" or "MP4" or "MKV" or "MOV" or "M4A" =>
                (Color.FromArgb(0x30, 0xEC, 0x48, 0x99), Color.FromArgb(0x55, 0xEC, 0x48, 0x99), Color.FromArgb(0xFF, 0xF4, 0x72, 0xB6)),
            _ =>
                (Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xFF, 0xE5, 0xE7, 0xEB))
        };
    }
}
