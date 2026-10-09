using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Pulse.Controls;

public sealed partial class PulseSeekBar : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(PulseSeekBar),
            new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty CanSeekProperty =
        DependencyProperty.Register(
            nameof(CanSeek),
            typeof(bool),
            typeof(PulseSeekBar),
            new PropertyMetadata(true, OnCanSeekChanged));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(Brush),
            typeof(PulseSeekBar),
            new PropertyMetadata(new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 132, 255)), (d, e) =>
            {
                if (d is PulseSeekBar sb && e.NewValue is Brush brush)
                {
                    sb.TrackProgress.Background = brush;
                }
            }));

    public static readonly DependencyProperty TrackBackgroundBrushProperty =
        DependencyProperty.Register(
            nameof(TrackBackgroundBrush),
            typeof(Brush),
            typeof(PulseSeekBar),
            new PropertyMetadata(new SolidColorBrush(Windows.UI.Color.FromArgb(45, 255, 255, 255)), (d, e) =>
            {
                if (d is PulseSeekBar sb && e.NewValue is Brush brush)
                {
                    sb.TrackBackground.Background = brush;
                }
            }));

    public static readonly DependencyProperty ThumbBrushProperty =
        DependencyProperty.Register(
            nameof(ThumbBrush),
            typeof(Brush),
            typeof(PulseSeekBar),
            new PropertyMetadata(new SolidColorBrush(Colors.White), (d, e) =>
            {
                if (d is PulseSeekBar sb && e.NewValue is Brush brush)
                {
                    sb.ThumbKnob.Background = brush;
                }
            }));

    private bool _isDragging;
    private bool _isHovered;

    public event EventHandler<double>? ScrubStarted;
    public event EventHandler<double>? ScrubDelta;
    public event EventHandler<double>? ScrubCompleted;

    public PulseSeekBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (AccentBrush is not null) TrackProgress.Background = AccentBrush;
        if (TrackBackgroundBrush is not null) TrackBackground.Background = TrackBackgroundBrush;
        if (ThumbBrush is not null) ThumbKnob.Background = ThumbBrush;
        UpdateVisuals(Value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0.0, 100.0));
    }

    public bool CanSeek
    {
        get => (bool)GetValue(CanSeekProperty);
        set => SetValue(CanSeekProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush TrackBackgroundBrush
    {
        get => (Brush)GetValue(TrackBackgroundBrushProperty);
        set => SetValue(TrackBackgroundBrushProperty, value);
    }

    public Brush ThumbBrush
    {
        get => (Brush)GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVisuals(Value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PulseSeekBar seekBar && !seekBar._isDragging)
        {
            seekBar.UpdateVisuals((double)e.NewValue);
        }
    }

    private static void OnCanSeekChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PulseSeekBar seekBar)
        {
            seekBar.Opacity = (bool)e.NewValue ? 1.0 : 0.45;
        }
    }

    private void UpdateVisuals(double percent)
    {
        if (ActualWidth <= 0)
        {
            return;
        }

        var clamped = Math.Clamp(percent, 0.0, 100.0);
        var progressWidth = (clamped / 100.0) * ActualWidth;
        TrackProgress.Width = Math.Max(0, progressWidth);

        var thumbWidth = ThumbKnob.Width;
        var thumbLeft = Math.Clamp(progressWidth - (thumbWidth / 2.0), 0.0, Math.Max(0, ActualWidth - thumbWidth));
        Canvas.SetLeft(ThumbKnob, thumbLeft);
    }

    private void SetTrackThickness(bool expanded)
    {
        var height = expanded ? 6.0 : 4.0;
        var cornerRadius = new CornerRadius(height / 2.0);
        TrackBackground.Height = height;
        TrackBackground.CornerRadius = cornerRadius;
        TrackProgress.Height = height;
        TrackProgress.CornerRadius = cornerRadius;

        ThumbKnob.Width = expanded ? 12.0 : 10.0;
        ThumbKnob.Height = expanded ? 12.0 : 10.0;
        ThumbKnob.CornerRadius = new CornerRadius(ThumbKnob.Width / 2.0);
        Canvas.SetTop(ThumbKnob, (22.0 - ThumbKnob.Height) / 2.0);
        ThumbKnob.Opacity = expanded ? 1.0 : 0.85;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = true;
        SetTrackThickness(true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = false;
        if (!_isDragging)
        {
            SetTrackThickness(false);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CanSeek || ActualWidth <= 0)
        {
            return;
        }

        _isDragging = true;
        CapturePointer(e.Pointer);
        SetTrackThickness(true);

        var point = e.GetCurrentPoint(this).Position;
        var percent = Math.Clamp((point.X / ActualWidth) * 100.0, 0.0, 100.0);
        UpdateVisuals(percent);
        Value = percent;
        ScrubStarted?.Invoke(this, percent);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(this).Position;
        var percent = Math.Clamp((point.X / ActualWidth) * 100.0, 0.0, 100.0);
        UpdateVisuals(percent);
        Value = percent;
        ScrubDelta?.Invoke(this, percent);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleasePointerCapture(e.Pointer);

        if (!_isHovered)
        {
            SetTrackThickness(false);
        }

        var point = e.GetCurrentPoint(this).Position;
        var percent = Math.Clamp((point.X / ActualWidth) * 100.0, 0.0, 100.0);
        UpdateVisuals(percent);
        Value = percent;
        ScrubCompleted?.Invoke(this, percent);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        if (!_isHovered)
        {
            SetTrackThickness(false);
        }

        ScrubCompleted?.Invoke(this, Value);
    }
}
