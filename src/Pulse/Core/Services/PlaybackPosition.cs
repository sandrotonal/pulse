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
}
