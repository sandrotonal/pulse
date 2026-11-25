using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Pulse.Core.Animation;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Services.Glass;
using Pulse.Services.System;
using Pulse.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Pulse.Views.Island;

public sealed partial class IslandWindow
{
    // ---- Drop Shelf ("Raf") Magnetic Drag-In / Tactile Drag-Out Physics ----

    private void OnSurfaceDragEnter(object sender, DragEventArgs e)
    {
        if (_isIslandHidden || _isDraggingOut)
        {
            return;
        }

        try
        {
            if (!ShelfShellService.CanAcceptDrop(e.DataView))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Rafa Bırak";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;

            _dragLeaveTimer?.Stop();
            _collapseDelayTimer.Stop();

            if (!_isDragHovering)
            {
                _wasExpandedBeforeDrag = _viewModel.IsExpanded;
                _isDragHovering = true;
                _viewModel.IsDragHovering = true;
            }

            // Magnetically spring open to Page 3 ("Raf") on drag approach.
            if (!_viewModel.IsExpanded)
            {
                _targetPageIndex = 3;
                _viewModel.SelectPage(3);
                ExpandFromCompact();
            }
            else if (_viewModel.SelectedPageIndex != 3)
            {
                NavigateToPage(3, isForward: true);
            }

            SetDropZonePulseState(active: true);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.OnSurfaceDragEnter", ex);
        }
    }

    private void OnSurfaceDragOver(object sender, DragEventArgs e)
    {
        if (_isIslandHidden || _isDraggingOut)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        try
        {
            if (!ShelfShellService.CanAcceptDrop(e.DataView))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Rafa Bırak";
            e.DragUIOverride.IsCaptionVisible = true;
            _dragLeaveTimer?.Stop();
            _collapseDelayTimer.Stop();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.OnSurfaceDragOver", ex);
        }
    }

    private void OnSurfaceDragLeave(object sender, DragEventArgs e)
    {
        if (!_isDragHovering)
        {
            return;
        }

        // Brief 180ms grace timer prevents flicker when cursor crosses internal child controls during drag.
        _dragLeaveTimer?.Stop();
        _dragLeaveTimer?.Start();
    }

    private void OnDragLeaveTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!_isDragHovering)
        {
            return;
        }

        _isDragHovering = false;
        _viewModel.IsDragHovering = false;
        SetDropZonePulseState(active: false);

        if (!_wasExpandedBeforeDrag && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_isPointerOverSurface)
        {
            _viewModel.Collapse();
        }
    }

    private async void OnSurfaceDrop(object sender, DragEventArgs e)
    {
        if (_isDraggingOut)
        {
            return;
        }

        _dragLeaveTimer?.Stop();
        _isDragHovering = false;
        _viewModel.IsDragHovering = false;
        SetDropZonePulseState(active: false);

        var deferral = e.GetDeferral();
        try
        {
            var added = await ShelfShellService.IngestDropAsync(e.DataView, _viewModel.DropShelf);
            if (added > 0)
            {
                TriggerDropAbsorptionPhysics();
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.OnSurfaceDrop", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnShelfItemDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ShelfItemViewModel item)
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            _isDraggingOut = true;
            _collapseDelayTimer.Stop();
            fe.Opacity = 0.45;
            var ok = await ShelfShellService.PopulateOutgoingDataPackageAsync(args.Data, [item.FilePath]);
            if (!ok)
            {
                args.Cancel = true;
                fe.Opacity = 1.0;
                _isDraggingOut = false;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.OnShelfItemDragStarting", ex);
            args.Cancel = true;
            fe.Opacity = 1.0;
            _isDraggingOut = false;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnShelfDragAllStarting(UIElement sender, DragStartingEventArgs args)
    {
        var paths = _viewModel.GetShelfExistingPaths();
        if (paths.Count == 0)
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            _isDraggingOut = true;
            _collapseDelayTimer.Stop();
            if (sender is FrameworkElement fe)
            {
                fe.Opacity = 0.55;
            }

            var ok = await ShelfShellService.PopulateOutgoingDataPackageAsync(args.Data, paths);
            if (!ok)
            {
                args.Cancel = true;
                if (sender is FrameworkElement fe2)
                {
                    fe2.Opacity = 1.0;
                }
                _isDraggingOut = false;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.OnShelfDragAllStarting", ex);
            args.Cancel = true;
            if (sender is FrameworkElement fe)
            {
                fe.Opacity = 1.0;
            }
            _isDraggingOut = false;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnShelfItemDropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        if (sender is FrameworkElement fe)
        {
            fe.Opacity = 1.0;
        }

        _isDraggingOut = false;
        _isInteractingWithControl = false;

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
        {
            RestartCollapseDelayTimer();
        }
    }

    private void OnShelfCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || ReducedMotion)
        {
            return;
        }

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(card, true);
            var visual = ElementCompositionPreview.GetElementVisual(card);
            var compositor = visual.Compositor;
            var w = card.ActualWidth > 1 ? (float)card.ActualWidth : 82f;
            var h = card.ActualHeight > 1 ? (float)card.ActualHeight : 98f;
            visual.CenterPoint = new Vector3(w / 2f, h / 2f, 0f);

            var overshoot = compositor.CreateCubicBezierEasingFunction(new Vector2(0.17f, 0.89f), new Vector2(0.32f, 1.22f));

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = TimeSpan.FromMilliseconds(240 * AnimationProfile.DurationScale);
            scaleAnim.InsertKeyFrame(0f, new Vector3(0.78f, 0.78f, 1f));
            scaleAnim.InsertKeyFrame(1f, Vector3.One, overshoot);
            visual.StartAnimation("Scale", scaleAnim);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(180 * AnimationProfile.DurationScale);
            opacityAnim.InsertKeyFrame(0f, 0f);
            opacityAnim.InsertKeyFrame(1f, 1f);
            visual.StartAnimation("Opacity", opacityAnim);

            var transAnim = compositor.CreateVector3KeyFrameAnimation();
            transAnim.Duration = TimeSpan.FromMilliseconds(240 * AnimationProfile.DurationScale);
            transAnim.InsertKeyFrame(0f, new Vector3(0f, 10f, 0f));
            transAnim.InsertKeyFrame(1f, Vector3.Zero, overshoot);
            visual.StartAnimation("Translation", transAnim);
        }
        catch
        {
        }
    }

    private void OnShelfCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card)
        {
            return;
        }

        AnimateShelfCardHover(card, hovered: true);
    }

    private void OnShelfCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card)
        {
            return;
        }

        AnimateShelfCardHover(card, hovered: false);
    }

    private void AnimateShelfCardHover(Border card, bool hovered)
    {
        try
        {
            var removeBtn = FindDescendantByName<Button>(card, "CardRemoveButton");
            if (removeBtn is not null)
            {
                if (ReducedMotion)
                {
                    removeBtn.Opacity = hovered ? 1.0 : 0.0;
                }
                else
                {
                    var btnVisual = ElementCompositionPreview.GetElementVisual(removeBtn);
                    var fade = btnVisual.Compositor.CreateScalarKeyFrameAnimation();
                    fade.Duration = TimeSpan.FromMilliseconds(110);
                    fade.InsertKeyFrame(1f, hovered ? 1f : 0f);
                    btnVisual.StartAnimation("Opacity", fade);
                    removeBtn.Opacity = hovered ? 1.0 : 0.0;
                }
            }

            var copyBtn = FindDescendantByName<Button>(card, "CardCopyButton");
            if (copyBtn is not null)
            {
                if (ReducedMotion)
                {
                    copyBtn.Opacity = hovered ? 1.0 : 0.0;
                }
                else
                {
                    var btnVisual = ElementCompositionPreview.GetElementVisual(copyBtn);
                    var fade = btnVisual.Compositor.CreateScalarKeyFrameAnimation();
                    fade.Duration = TimeSpan.FromMilliseconds(110);
                    fade.InsertKeyFrame(1f, hovered ? 1f : 0f);
                    btnVisual.StartAnimation("Opacity", fade);
                    copyBtn.Opacity = hovered ? 1.0 : 0.0;
                }
            }

            if (ReducedMotion)
            {
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(card, true);
            var visual = ElementCompositionPreview.GetElementVisual(card);
            var compositor = visual.Compositor;
            var w = card.ActualWidth > 1 ? (float)card.ActualWidth : 82f;
            var h = card.ActualHeight > 1 ? (float)card.ActualHeight : 98f;
            visual.CenterPoint = new Vector3(w / 2f, h / 2f, 0f);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = TimeSpan.FromMilliseconds(hovered ? 140 : 165);
            scaleAnim.InsertKeyFrame(1f, hovered ? new Vector3(1.045f, 1.045f, 1f) : Vector3.One, easing);
            visual.StartAnimation("Scale", scaleAnim);

            var liftAnim = compositor.CreateVector3KeyFrameAnimation();
            liftAnim.Duration = TimeSpan.FromMilliseconds(hovered ? 140 : 165);
            liftAnim.InsertKeyFrame(1f, hovered ? new Vector3(0f, -2.5f, 0f) : Vector3.Zero, easing);
            visual.StartAnimation("Translation", liftAnim);
        }
        catch
        {
        }
    }

    private void OnShelfCardDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShelfItemViewModel item)
        {
            e.Handled = true;
            _viewModel.OpenShelfItemInExplorer(item.FilePath);
        }
    }

    private async void OnShelfCardCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && !string.IsNullOrWhiteSpace(path))
        {
            await _viewModel.CopySingleShelfItemAsync(path);
        }
    }

    private void OnShelfCardRevealClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && !string.IsNullOrWhiteSpace(path))
        {
            _viewModel.OpenShelfItemInExplorer(path);
        }
    }

    private async void OnShelfRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string itemId || string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        // Tactile shrink-out before removal when clicked from the card's X badge.
        if (!ReducedMotion && sender is Button btn)
        {
            var card = FindAncestorBorder(btn);
            if (card is not null)
            {
                try
                {
                    var visual = ElementCompositionPreview.GetElementVisual(card);
                    var compositor = visual.Compositor;
                    var quick = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));

                    var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                    scaleAnim.Duration = TimeSpan.FromMilliseconds(105);
                    scaleAnim.InsertKeyFrame(1f, new Vector3(0.72f, 0.72f, 1f), quick);
                    visual.StartAnimation("Scale", scaleAnim);

                    var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
                    fadeAnim.Duration = TimeSpan.FromMilliseconds(105);
                    fadeAnim.InsertKeyFrame(1f, 0f, quick);
                    visual.StartAnimation("Opacity", fadeAnim);

                    await Task.Delay(105);
                }
                catch
                {
                }
            }
        }

        if (!_windowClosed)
        {
            _viewModel.RemoveShelfItem(itemId);
        }
    }

    private async void OnShelfBrowseFilesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                var paths = files.Select(f => f.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                _viewModel.AddShelfPaths(paths);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.OnShelfBrowseFilesClick", ex);
        }
    }

    private static T? FindDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
            {
                return match;
            }

            var nested = FindDescendantByName<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Border? FindAncestorBorder(DependencyObject child)
    {
        var current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is Border b && b.CanDrag)
            {
                return b;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>
    /// Two-stage volume-preserving Apple squash -> elastic rebound -> settle physics
    /// paired with a luminous rim flash when a file lands in the Drop Shelf.
    /// </summary>
    private void TriggerDropAbsorptionPhysics()
    {
        if (ReducedMotion)
        {
            return;
        }

        try
        {
            // Stage 1: Squash impact
            AnimateSurfaceScale(1.024f, 0.955f, 75);

            // Luminous rim flash
            if (ShelfDropFlashBorder is not null)
            {
                var flashVisual = ElementCompositionPreview.GetElementVisual(ShelfDropFlashBorder);
                var flashAnim = flashVisual.Compositor.CreateScalarKeyFrameAnimation();
                flashAnim.Duration = TimeSpan.FromMilliseconds(360);
                flashAnim.InsertKeyFrame(0f, 0.72f);
                flashAnim.InsertKeyFrame(0.25f, 0.55f);
                flashAnim.InsertKeyFrame(1f, 0f);
                flashVisual.StartAnimation("Opacity", flashAnim);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // Stage 2: Elastic rebound overshoot
                    await Task.Delay(78).ConfigureAwait(false);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_windowClosed)
                        {
                            AnimateSurfaceScale(0.992f, 1.012f, 110);
                        }
                    });

                    // Stage 3: Settle smoothly to rest
                    await Task.Delay(112).ConfigureAwait(false);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_windowClosed)
                        {
                            AnimateSurfaceScale(1.0f, 1.0f, 180);
                        }
                    });
                }
                catch
                {
                }
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.TriggerDropAbsorptionPhysics", ex);
        }
    }

    private void SetDropZonePulseState(bool active)
    {
        if (ReducedMotion)
        {
            return;
        }

        try
        {
            if (ShelfDropZoneBorder is not null)
            {
                AnimateElementScale(ShelfDropZoneBorder, active ? 1.02f : 1.0f, active ? 150 : 170);
            }

            if (ShelfActiveDropOverlay is not null)
            {
                var overlayVisual = ElementCompositionPreview.GetElementVisual(ShelfActiveDropOverlay);
                var overlayFade = overlayVisual.Compositor.CreateScalarKeyFrameAnimation();
                overlayFade.Duration = TimeSpan.FromMilliseconds(active ? 150 : 130);
                overlayFade.InsertKeyFrame(1f, active ? 1f : 0f);
                overlayVisual.StartAnimation("Opacity", overlayFade);
            }

            if (ShelfDropBobIcon is not null)
            {
                var bobVisual = ElementCompositionPreview.GetElementVisual(ShelfDropBobIcon);
                if (active)
                {
                    var compositor = bobVisual.Compositor;
                    var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));
                    var bob = compositor.CreateVector3KeyFrameAnimation();
                    bob.Duration = TimeSpan.FromMilliseconds(720);
                    bob.IterationBehavior = AnimationIterationBehavior.Forever;
                    bob.Direction = AnimationDirection.Alternate;
                    bob.InsertKeyFrame(0f, new Vector3(0f, -3.5f, 0f));
                    bob.InsertKeyFrame(1f, new Vector3(0f, 3.5f, 0f), ease);
                    bobVisual.StartAnimation("Translation", bob);
                }
                else
                {
                    bobVisual.StopAnimation("Translation");
                    ShelfDropBobIcon.Translation = Vector3.Zero;
                }
            }
        }
        catch
        {
        }
    }
}
