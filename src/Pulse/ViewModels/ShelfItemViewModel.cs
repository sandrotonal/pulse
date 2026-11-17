using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Models;
using Pulse.Services.System;

namespace Pulse.ViewModels;

/// <summary>
/// Presentation wrapper for a staged Drop Shelf item with optional decoded thumbnail.
/// </summary>
public sealed class ShelfItemViewModel
{
    public ShelfItemViewModel(ShelfItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Thumbnail = ShelfShellService.TryCreateThumbnail(model);
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
}
