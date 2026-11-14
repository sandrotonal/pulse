namespace Pulse.Core.Services;

public static class PlaybackPosition
{
    public static TimeSpan Clamp(TimeSpan position, TimeSpan? duration)
    {
        var maximum = duration is { Ticks: > 0 } value ? value.Ticks : long.MaxValue;
        return TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, maximum));
    }

    public static TimeSpan Offset(TimeSpan position, TimeSpan delta, TimeSpan? duration)
    {
        var target = position.Ticks;
        try
        {
            target = checked(target + delta.Ticks);
        }
        catch (OverflowException)
        {
            target = delta.Ticks > 0 ? long.MaxValue : long.MinValue;
        }

        return Clamp(TimeSpan.FromTicks(target), duration);
    }

    public static string FormatTime(TimeSpan time)
    {
        var clamped = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        return clamped.TotalHours >= 1
            ? clamped.ToString(@"h\:mm\:ss")
            : clamped.ToString(@"m\:ss");
    }

    public static string ComputePreviewText(TimeSpan? duration, double percent)
    {
        var clampedPercent = Math.Clamp(percent, 0.0, 100.0);
        if (duration is { Ticks: > 0 } dur)
        {
            var previewMs = dur.TotalMilliseconds * (clampedPercent / 100.0);
            return FormatTime(TimeSpan.FromMilliseconds(previewMs));
        }

        return $"{Math.Round(clampedPercent)}%";
    }
}
