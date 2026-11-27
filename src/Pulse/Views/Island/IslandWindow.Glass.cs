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
    // Theme Management
    // Apple "Liquid Glass": Microsoft's DesktopAcrylicBackdrop conflicts with the
    // DWM transparency trick WinUIEx uses, so when glass is enabled we render our own
    // frost layer: sample the live desktop behind the island, blur it and tint it
    // in-app. Never touches SystemBackdrop (keeps the TransparentTintBackdrop wired
    // in XAML so the window stays alpha-capable).
    public void ApplyGlass(bool deferCapture = false)
    {
        _glassEnabled = _viewModel.UseTransparency;
        if (!_glassEnabled)
        {
            _frostRefreshTimer?.Stop();
            _frostGeneration++;
            _hasFrostBitmap = false;
            if (FrostLayer is not null)
            {
                FrostLayer.Fill = null;
            }
            if (DetachedFrostLayer is not null)
            {
                DetachedFrostLayer.Fill = null;
            }
            _detachedFrostBrush = null;
        }

        UpdateFrostVisibility();
        if (_glassEnabled)
        {
            // No periodic re-capture: each capture briefly hides the window, which
            // reads as a visible blink. Deferring capture on startup lets the first
            // obsidian frame paint immediately without a hide/show race on boot.
            if (deferCapture)
            {
                ScheduleFrostRefresh(delayMs: 380);
            }
            else
            {
                _ = RefreshFrostAsync(force: true);
            }
        }
    }

    private void ScheduleFrostRefresh(int delayMs = 240)
    {
        if (!_glassEnabled || _isIslandHidden)
        {
            return;
        }

        if (_frostRefreshTimer is null)
        {
            _frostRefreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _frostRefreshTimer.IsRepeating = false;
            _frostRefreshTimer.Tick += (_, _) =>
            {
                if (_glassEnabled && !_isIslandHidden)
                {
                    _ = RefreshFrostAsync(force: true);
                }
            };
        }

        _frostRefreshTimer.Stop();
        _frostRefreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(80, delayMs));
        _frostRefreshTimer.Start();
    }

    private void OnWindowRootActualThemeChanged(FrameworkElement sender, object args)
    {
        if (string.Equals(_viewModel.Theme, "System", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_viewModel.Theme))
        {
            ApplyTheme(_viewModel.Theme);
            _viewModel.RefreshThemeResources();
        }
    }

    private void UpdateFrostVisibility()
    {
        var isDynamicMusic = _viewModel.DynamicColorTint && _viewModel.HasTrack;
        var visible = (_glassEnabled && !isDynamicMusic) ? Visibility.Visible : Visibility.Collapsed;
        FrostLayer.Visibility = visible;
        if (FrostTintLayer is not null)
        {
            FrostTintLayer.Visibility = visible;
        }
        var sheenVisible = (_glassEnabled && !isDynamicMusic) ? Visibility.Visible : Visibility.Collapsed;
        if (FrostSheen is not null)
        {
            FrostSheen.Visibility = sheenVisible;
        }
        if (DetachedFrostLayer is not null)
        {
            DetachedFrostLayer.Visibility = visible;
        }
        if (DetachedFrostTintLayer is not null)
        {
            DetachedFrostTintLayer.Visibility = visible;
        }
        if (DetachedFrostSheen is not null)
        {
            DetachedFrostSheen.Visibility = sheenVisible;
        }
        if (_glassEnabled)
        {
            UpdateFrostTint(ComputeCurrentPalette());
        }
        UpdateArtworkAmbientGlow(ComputeCurrentPalette());
    }

    private GlassSurfacePalette ComputeCurrentPalette()
    {
        var isLight = DesktopFrostService.ResolveIsLightTheme(_viewModel.Theme);
        var hasFrostBitmap = _glassEnabled && _hasFrostBitmap && FrostLayer.Fill is Microsoft.UI.Xaml.Media.ImageBrush;
        var effectiveArtworkAccent = _viewModel.HasTrack ? _viewModel.ArtworkAccentColor : 0u;
        return LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: isLight,
            useTransparency: _glassEnabled,
            transparencyLevel: _viewModel.TransparencyLevel,
            dynamicColorTint: _viewModel.DynamicColorTint,
            artworkAccentColor: effectiveArtworkAccent,
            artworkTintStrength: _viewModel.ArtworkTintStrength,
            hasFrostBitmap: hasFrostBitmap);
    }

    /// <summary>Recolors the live glass tint and specular sheen without touching the captured blur.</summary>
    private void UpdateFrostTint(GlassSurfacePalette palette)
    {
        var hasBitmap = _glassEnabled && _hasFrostBitmap && FrostLayer.Fill is Microsoft.UI.Xaml.Media.ImageBrush;
        if (FrostTintLayer is not null)
        {
            FrostTintLayer.Fill = DesktopFrostService.CreateBrush(palette.FrostTintRgb);
            FrostTintLayer.Opacity = hasBitmap ? palette.FrostTintOpacity : 0.0;
        }

        if (DetachedFrostTintLayer is not null)
        {
            DetachedFrostTintLayer.Fill = DesktopFrostService.CreateBrush(palette.FrostTintRgb);
            DetachedFrostTintLayer.Opacity = hasBitmap ? palette.FrostTintOpacity : 0.0;
        }

        if (FrostSheenTopStop is not null)
        {
            FrostSheenTopStop.Color = DesktopFrostService.ToColor(palette.SheenTopArgb);
        }

        if (DetachedFrostSheenTopStop is not null)
        {
            DetachedFrostSheenTopStop.Color = DesktopFrostService.ToColor(palette.SheenTopArgb);
        }
    }

    private void EnsureAmbientBrush()
    {
        if (_ambientBrush is not null)
        {
            if (ArtworkAmbientLayer is not null && ArtworkAmbientLayer.Fill != _ambientBrush)
            {
                ArtworkAmbientLayer.Fill = _ambientBrush;
            }
            if (DetachedArtworkAmbientLayer is not null && DetachedArtworkAmbientLayer.Fill != _ambientBrush)
            {
                DetachedArtworkAmbientLayer.Fill = _ambientBrush;
            }
            return;
        }

        if (ArtworkAmbientLayer is null)
        {
            return;
        }

        _ambientStartStop = new Microsoft.UI.Xaml.Media.GradientStop { Offset = 0.0 };
        _ambientMidStop = new Microsoft.UI.Xaml.Media.GradientStop { Offset = 0.45 };
        _ambientEndStop = new Microsoft.UI.Xaml.Media.GradientStop { Offset = 1.0 };

        _ambientBrush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.0, 0.0),
            EndPoint = new Windows.Foundation.Point(0.85, 0.95)
        };
        _ambientBrush.GradientStops.Add(_ambientStartStop);
        _ambientBrush.GradientStops.Add(_ambientMidStop);
        _ambientBrush.GradientStops.Add(_ambientEndStop);

        ArtworkAmbientLayer.Fill = _ambientBrush;
        if (DetachedArtworkAmbientLayer is not null)
        {
            DetachedArtworkAmbientLayer.Fill = _ambientBrush;
        }
    }

    private void UpdateArtworkAmbientGlow(GlassSurfacePalette palette)
    {
        try
        {
            if (ArtworkAmbientLayer is null)
            {
                return;
            }

            EnsureAmbientBrush();

            var isDynamicMusic = _viewModel.DynamicColorTint && _viewModel.HasTrack;
            _isAmbientTargetDynamicMusic = isDynamicMusic;

            if (!isDynamicMusic)
            {
                if (ReducedMotion)
                {
                    _isAmbientColorAnimating = false;
                    _currentAmbientAlpha = 0;
                    _targetAmbientAlpha = 0;
                    ArtworkAmbientLayer.Visibility = Visibility.Collapsed;
                    if (DetachedArtworkAmbientLayer is not null)
                    {
                        DetachedArtworkAmbientLayer.Visibility = Visibility.Collapsed;
                    }
                    if (_glassEnabled)
                    {
                        SetFrostLayersVisibility(Visibility.Visible);
                    }
                    StopRenderLoopIfIdle();
                    return;
                }

                _targetAmbientAlpha = 0.0;
                if (_currentAmbientAlpha > 0.5)
                {
                    _isAmbientColorAnimating = true;
                    EnsureRenderLoopRunning();
                }
                else
                {
                    _isAmbientColorAnimating = false;
                    ArtworkAmbientLayer.Visibility = Visibility.Collapsed;
                    if (DetachedArtworkAmbientLayer is not null)
                    {
                        DetachedArtworkAmbientLayer.Visibility = Visibility.Collapsed;
                    }
                    if (_glassEnabled)
                    {
                        SetFrostLayersVisibility(Visibility.Visible);
                    }
                    StopRenderLoopIfIdle();
                }
                return;
            }

            // Suppress desktop wallpaper capture to avoid any bleed
            SetFrostLayersVisibility(Visibility.Collapsed);

            ArtworkAmbientLayer.Visibility = Visibility.Visible;
            if (DetachedArtworkAmbientLayer is not null)
            {
                DetachedArtworkAmbientLayer.Visibility = Visibility.Visible;
            }

            var accentColor = DesktopFrostService.ToColor(palette.AccentArgb);
            var strength = Math.Clamp(_viewModel.ArtworkTintStrength / 100.0, 0.2, 1.0);
            var isLight = palette.IsLightTheme;

            var targetAlpha = (double)Math.Clamp(Math.Round((isLight ? 75 : 125) * strength), 20, 220);

            _targetAmbientR = accentColor.R;
            _targetAmbientG = accentColor.G;
            _targetAmbientB = accentColor.B;
            _targetAmbientAlpha = targetAlpha;

            if (ReducedMotion)
            {
                _currentAmbientR = _targetAmbientR;
                _currentAmbientG = _targetAmbientG;
                _currentAmbientB = _targetAmbientB;
                _currentAmbientAlpha = _targetAmbientAlpha;
                _isAmbientColorAnimating = false;
                ApplyAmbientStops();
                StopRenderLoopIfIdle();
                return;
            }

            // Initial transition into dynamic music starts at 0 alpha and fades in smoothly
            if (_currentAmbientAlpha <= 1.0)
            {
                _currentAmbientR = _targetAmbientR;
                _currentAmbientG = _targetAmbientG;
                _currentAmbientB = _targetAmbientB;
            }

            if (Math.Abs(_targetAmbientR - _currentAmbientR) > 0.5 ||
                Math.Abs(_targetAmbientG - _currentAmbientG) > 0.5 ||
                Math.Abs(_targetAmbientB - _currentAmbientB) > 0.5 ||
                Math.Abs(_targetAmbientAlpha - _currentAmbientAlpha) > 0.5)
            {
                _isAmbientColorAnimating = true;
                EnsureRenderLoopRunning();
            }
            else
            {
                _currentAmbientR = _targetAmbientR;
                _currentAmbientG = _targetAmbientG;
                _currentAmbientB = _targetAmbientB;
                _currentAmbientAlpha = _targetAmbientAlpha;
                _isAmbientColorAnimating = false;
                ApplyAmbientStops();
                StopRenderLoopIfIdle();
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.UpdateArtworkAmbientGlow", ex);
        }
    }

    private void StepAmbientColorTransition(double dt)
    {
        // Smooth exponential interpolation with ~120ms time constant (reaches 95% in ~360ms)
        var factor = 1.0 - Math.Exp(-dt / 0.12);

        _currentAmbientR += (_targetAmbientR - _currentAmbientR) * factor;
        _currentAmbientG += (_targetAmbientG - _currentAmbientG) * factor;
        _currentAmbientB += (_targetAmbientB - _currentAmbientB) * factor;
        _currentAmbientAlpha += (_targetAmbientAlpha - _currentAmbientAlpha) * factor;

        var diffR = Math.Abs(_targetAmbientR - _currentAmbientR);
        var diffG = Math.Abs(_targetAmbientG - _currentAmbientG);
        var diffB = Math.Abs(_targetAmbientB - _currentAmbientB);
        var diffA = Math.Abs(_targetAmbientAlpha - _currentAmbientAlpha);

        if (diffR < 0.5 && diffG < 0.5 && diffB < 0.5 && diffA < 0.5)
        {
            _currentAmbientR = _targetAmbientR;
            _currentAmbientG = _targetAmbientG;
            _currentAmbientB = _targetAmbientB;
            _currentAmbientAlpha = _targetAmbientAlpha;
            _isAmbientColorAnimating = false;

            if (!_isAmbientTargetDynamicMusic)
            {
                ArtworkAmbientLayer.Visibility = Visibility.Collapsed;
                if (DetachedArtworkAmbientLayer is not null)
                {
                    DetachedArtworkAmbientLayer.Visibility = Visibility.Collapsed;
                }
                if (_glassEnabled)
                {
                    SetFrostLayersVisibility(Visibility.Visible);
                }
            }
        }

        ApplyAmbientStops();
    }

    private void SetFrostLayersVisibility(Visibility visibility)
    {
        FrostLayer.Visibility = visibility;
        if (FrostTintLayer is not null) FrostTintLayer.Visibility = visibility;
        if (FrostSheen is not null) FrostSheen.Visibility = visibility;
        if (DetachedFrostLayer is not null) DetachedFrostLayer.Visibility = visibility;
        if (DetachedFrostTintLayer is not null) DetachedFrostTintLayer.Visibility = visibility;
        if (DetachedFrostSheen is not null) DetachedFrostSheen.Visibility = visibility;
    }

    private void ApplyAmbientStops()
    {
        if (_ambientStartStop is null || _ambientMidStop is null || _ambientEndStop is null)
        {
            return;
        }

        byte curAlpha = (byte)Math.Clamp(Math.Round(_currentAmbientAlpha), 0, 255);
        byte curR = (byte)Math.Clamp(Math.Round(_currentAmbientR), 0, 255);
        byte curG = (byte)Math.Clamp(Math.Round(_currentAmbientG), 0, 255);
        byte curB = (byte)Math.Clamp(Math.Round(_currentAmbientB), 0, 255);

        byte midAlpha = (byte)Math.Clamp(Math.Round(curAlpha * 0.48), 0, 255);

        _ambientStartStop.Color = Windows.UI.Color.FromArgb(curAlpha, curR, curG, curB);
        _ambientMidStop.Color = Windows.UI.Color.FromArgb(midAlpha, curR, curG, curB);
        _ambientEndStop.Color = Windows.UI.Color.FromArgb(0, curR, curG, curB);
    }

    private void PlayArtworkTransitionAnimation()
    {
        if (ReducedMotion)
        {
            return;
        }

        try
        {
            if (_viewModel.IsExpanded && ExpandedArtImage is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(ExpandedArtImage);
                var compositor = visual.Compositor;
                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(0.0f, 0.45f);
                fade.InsertKeyFrame(1.0f, 1.0f);
                fade.Duration = TimeSpan.FromMilliseconds(220);
                visual.StartAnimation("Opacity", fade);
            }
            else if (!_viewModel.IsExpanded && CompactArtImage is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(CompactArtImage);
                var compositor = visual.Compositor;
                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(0.0f, 0.45f);
                fade.InsertKeyFrame(1.0f, 1.0f);
                fade.Duration = TimeSpan.FromMilliseconds(180);
                visual.StartAnimation("Opacity", fade);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.PlayArtworkTransitionAnimation", ex);
        }
    }

    private bool _frostBusy;

    /// <summary>Samples the desktop area under the island, blurs + tints it into the frost brush.</summary>
    private async Task RefreshFrostAsync(bool force = false)
    {
        if (_windowClosed || !_glassEnabled || _isIslandHidden || _frostBusy || FrostLayer is null)
        {
            return;
        }

        if (!force && _isPointerOverSurface)
        {
            return;
        }

        _frostBusy = true;
        var generation = ++_frostGeneration;
        var windowWasTemporarilyHidden = false;
        try
        {
            GetClientRect(_hwnd, out var crc);
            var pt = new Win32Point();
            _ = ClientToScreen(_hwnd, ref pt);
            var dpiScale = _dpiScale > 0 ? _dpiScale : 1.0;
            var surfaceInsetPx = (int)Math.Round(IslandSurfaceLayout.SurfaceTopInsetDip * dpiScale);
            var screenX = pt.X;
            var screenY = pt.Y + surfaceInsetPx;
            var clientW = Math.Max(16, crc.Right - crc.Left);
            var remainingH = Math.Max(16, (crc.Bottom - crc.Top) - surfaceInsetPx);
            var targetSurfaceH = (int)Math.Round(IslandSurfaceLayout.DefaultHostHeightDip * dpiScale);
            var clientH = Math.Max(16, Math.Min(remainingH, targetSurfaceH));

            if (clientW < 16 || clientH < 16)
            {
                return;
            }

            // Hide briefly on the UI thread (owner thread of _hwnd) so the captured pixels contain only desktop content.
            windowWasTemporarilyHidden = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpHideWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
            await Task.Delay(45);

            // Capture raw desktop pixels off-UI thread, then restore the island HWND immediately
            // BEFORE Gaussian blur + PNG encoding so the window is only hidden for ~45ms.
            global::System.Drawing.Bitmap? capturedBmp = null;
            if (!_windowClosed && !_isIslandHidden)
            {
                capturedBmp = await Task.Run(() => DesktopFrostService.CaptureScreenRegion(screenX, screenY, clientW, clientH));
            }

            if (windowWasTemporarilyHidden && !_windowClosed && !_isIslandHidden)
            {
                _frostRestoreGuardTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                var insertAfter = GetTargetZOrderInsertAfter(out var skipZOrder);
                _ = SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, SwpShowWindow | SwpNoActivate | SwpNoMove | SwpNoSize | (skipZOrder ? SwpNoZOrder : 0));
                windowWasTemporarilyHidden = false;
            }

            byte[]? pngBytes = null;
            if (capturedBmp is not null)
            {
                try
                {
                    if (!_windowClosed && !_isIslandHidden && generation == _frostGeneration)
                    {
                        pngBytes = await Task.Run(() =>
                            DesktopFrostService.ProcessCapturedFrostToPng(capturedBmp, dpiScale, blurRadius: 11, vibrancyBoost: 1.24));
                    }
                }
                finally
                {
                    capturedBmp.Dispose();
                }
            }

            if (_windowClosed || pngBytes is null || generation != _frostGeneration || _isIslandHidden || !_glassEnabled || FrostLayer is null)
            {
                return;
            }

            var image = await DesktopFrostService.DecodePngToBitmapImageAsync(pngBytes);
            if (_windowClosed || generation != _frostGeneration || _isIslandHidden || !_glassEnabled || FrostLayer is null)
            {
                return;
            }

            FrostLayer.Fill = new Microsoft.UI.Xaml.Media.ImageBrush
            {
                ImageSource = image,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
                AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Center,
                AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Top
            };
            if (DetachedFrostLayer is not null)
            {
                _detachedFrostTransform ??= new Microsoft.UI.Xaml.Media.TranslateTransform();
                _detachedFrostBrush = new Microsoft.UI.Xaml.Media.ImageBrush
                {
                    ImageSource = image,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
                    AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Left,
                    AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Top,
                    Transform = _detachedFrostTransform
                };
                DetachedFrostLayer.Fill = _detachedFrostBrush;
                UpdateDetachedFrostTransform(_currentTrailingX);
            }
            _hasFrostBitmap = true;
            ApplyTheme(_viewModel.Theme);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.RefreshFrostAsync", ex);
        }
        finally
        {
            if (windowWasTemporarilyHidden && !_windowClosed && !_isIslandHidden && _hwnd != IntPtr.Zero)
            {
                try
                {
                    _frostRestoreGuardTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    var insertAfter = GetTargetZOrderInsertAfter(out var skipZOrder);
                    _ = SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, SwpShowWindow | SwpNoActivate | SwpNoMove | SwpNoSize | (skipZOrder ? SwpNoZOrder : 0));
                }
                catch
                {
                }
            }

            _frostBusy = false;
        }
    }

    public void ApplyTheme(string theme)
    {
        var palette = ComputeCurrentPalette();
        var requestedTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? ElementTheme.Dark
            : string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? ElementTheme.Light
                : ElementTheme.Default;

        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "IslandBackground", palette.FallbackBackgroundArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "IslandSolidSurface", 0xFF000000u | (palette.FallbackBackgroundArgb & 0x00FFFFFFu));
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "IslandBorder", palette.BorderRimTopArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PrimaryText", palette.PrimaryTextArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SecondaryText", palette.SecondaryTextArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "DangerText", palette.IsLightTheme ? 0xFFD70015u : 0xFFFF453Au);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PillControlBackground", palette.PillControlBackgroundArgb);

        var sliderFill = palette.IsLightTheme ? 0xFF141518u : 0xFFFFFFFFu;
        var sliderFillPressed = palette.IsLightTheme ? 0xFF3A3A3Cu : 0xE6FFFFFFu;
        var sliderTrack = palette.IsLightTheme ? 0x24000000u : 0x26FFFFFFu;
        var sliderTrackHover = palette.IsLightTheme ? 0x33000000u : 0x36FFFFFFu;
        var sliderThumb = palette.IsLightTheme ? 0xFF141518u : 0xFFFFFFFFu;
        var sliderThumbPressed = palette.IsLightTheme ? 0xFF3A3A3Cu : 0xE6FFFFFFu;

        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackValueFill", sliderFill);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackValueFillPointerOver", sliderFill);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackValueFillPressed", sliderFillPressed);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackFill", sliderTrack);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackFillPointerOver", sliderTrackHover);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackFillPressed", sliderTrackHover);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderThumbBackground", sliderThumb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderThumbBackgroundPointerOver", sliderThumb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderThumbBackgroundPressed", sliderThumbPressed);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PlayButtonBackground", palette.PlayButtonBackgroundArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PlayButtonForeground", palette.PlayButtonForegroundArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "Accent", palette.AccentArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SeekAccent", palette.AccentArgb);

        // Drop Shelf theme-aware surface tokens
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfSurfaceSubtle", palette.IsLightTheme ? 0x0A000000u : 0x0CFFFFFFu);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfCardBackground", palette.IsLightTheme ? 0x0D000000u : 0x14FFFFFFu);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfCardBorder", palette.IsLightTheme ? 0x1A000000u : 0x1EFFFFFFu);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfThumbnailBackground", palette.IsLightTheme ? 0xFFEBEFF5u : 0xFF0A0B0Eu);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfActionBadgeBackground", palette.IsLightTheme ? 0xE6FFFFFFu : 0xE61A1B20u);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfDockBackground", palette.IsLightTheme ? 0x0A000000u : 0x0EFFFFFFu);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfDropOverlayBackground", palette.IsLightTheme ? 0xEBEEF4FAu : 0xE6111215u);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfDropOverlayBorder", palette.IsLightTheme ? 0x66000000u : 0x90FFFFFFu);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfActionButtonBackground", palette.IsLightTheme ? 0x12000000u : 0x16FFFFFFu);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "ShelfActionButtonBorder", palette.IsLightTheme ? 0x1E000000u : 0x24FFFFFFu);

        if (WindowRoot.RequestedTheme != requestedTheme)
        {
            WindowRoot.RequestedTheme = requestedTheme;
        }

        var isDynamicMusic = _viewModel.DynamicColorTint && _viewModel.HasTrack;
        var surfaceBg = isDynamicMusic ? palette.FallbackBackgroundArgb : palette.SurfaceBackgroundArgb;

        PillBackdrop.Background = DesktopFrostService.CreateBrush(surfaceBg);
        PillBackdrop.BorderBrush = DesktopFrostService.CreateBrush(0x00000000u);
        PillBackdrop.BorderThickness = new Thickness(0);

        UpdateArtworkAmbientGlow(palette);

        if (_frostRimShape is not null)
        {
            _frostRimShape.StrokeBrush = DesktopFrostService.CreateCompositionVerticalGradient(
                _pillMaskVisual.Compositor,
                palette.BorderRimTopArgb,
                palette.BorderRimBottomArgb);
        }

        if (HiddenAnchorPill is not null)
        {
            HiddenAnchorPill.Background = DesktopFrostService.CreateBrush(palette.HiddenAnchorArgb);
        }

        if (DetachedPill is not null)
        {
            DetachedPill.Background = DesktopFrostService.CreateBrush(surfaceBg);
            DetachedPill.BorderBrush = DesktopFrostService.CreateVerticalGradientBrush(palette.BorderRimTopArgb, palette.BorderRimBottomArgb);
            DetachedPill.BorderThickness = new Thickness(1);
            if (DetachedFrostSheenTopStop is not null)
            {
                DetachedFrostSheenTopStop.Color = DesktopFrostService.ToColor(palette.SheenTopArgb);
            }
        }

        WaveBar1.Background = DesktopFrostService.CreateBrush(palette.AccentArgb);
        WaveBar2.Background = DesktopFrostService.CreateBrush(palette.AccentArgb);
        WaveBar3.Background = DesktopFrostService.CreateBrush(palette.AccentArgb);
        if (ExpandedSeekBar is not null)
        {
            ExpandedSeekBar.AccentBrush = DesktopFrostService.CreateBrush(palette.AccentArgb);
            ExpandedSeekBar.TrackBackgroundBrush = DesktopFrostService.CreateBrush(palette.IsLightTheme ? 0x29000000u : 0x2DFFFFFFu);
            ExpandedSeekBar.ThumbBrush = DesktopFrostService.CreateBrush(palette.IsLightTheme ? 0xFF141518u : 0xFFFFFFFFu);
            ExpandedSeekBar.TooltipBackgroundBrush = DesktopFrostService.CreateBrush(palette.IsLightTheme ? 0xF2FFFFFFu : 0xE618181Au);
            ExpandedSeekBar.TooltipBorderBrush = DesktopFrostService.CreateBrush(palette.IsLightTheme ? 0x24000000u : 0x33FFFFFFu);
            ExpandedSeekBar.TooltipForegroundBrush = DesktopFrostService.CreateBrush(palette.IsLightTheme ? 0xFF141518u : 0xFFFFFFFFu);
        }

        if (_isDotAnimating)
        {
            UpdatePageDotBrushes();
        }
        else
        {
            UpdatePageDots(_viewModel.SelectedPageIndex, animated: false);
        }

        // The glass tint is a live overlay on the captured blur, so track/theme/slider
        // changes recolor it instantly without a desktop re-capture (no blink).
        UpdateFrostTint(palette);
    }

    private void UpdatePageDotBrushes()
    {
        if (WindowRoot.Resources.TryGetValue("PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
        {
            for (int i = 0; i < ExpandedPageCount; i++)
            {
                if (_dotVisualElements[i] is not null)
                {
                    _dotVisualElements[i].Background = brush;
                }
            }
        }
    }

}
