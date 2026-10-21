namespace Pulse.Core.Animation;

public sealed class DampedSpringOscillator
{
    public double DampingRatio { get; }
    public double FrequencyResponse { get; }

    private readonly double _omega0;
    private readonly double _omegaD;

    public DampedSpringOscillator(double dampingRatio = 0.82, double responseSeconds = 0.35)
    {
        DampingRatio = Math.Clamp(dampingRatio, 0.01, 2.0);
        FrequencyResponse = Math.Max(0.01, responseSeconds);

        _omega0 = (2.0 * Math.PI) / FrequencyResponse;

        if (DampingRatio < 1.0)
        {
            _omegaD = _omega0 * Math.Sqrt(1.0 - (DampingRatio * DampingRatio));
        }
        else
        {
            _omegaD = 0.0;
        }
    }

    public SpringState Evaluate(double elapsedSeconds, double start, double target, double initialVelocity = 0.0)
    {
        if (elapsedSeconds <= 0.0)
        {
            return new SpringState(start, initialVelocity, false);
        }

        double delta = start - target;

        if (Math.Abs(delta) < 0.0001 && Math.Abs(initialVelocity) < 0.0001)
        {
            return new SpringState(target, 0.0, true);
        }

        if (DampingRatio < 1.0)
        {
            double decay = Math.Exp(-DampingRatio * _omega0 * elapsedSeconds);
            double cos = Math.Cos(_omegaD * elapsedSeconds);
            double sin = Math.Sin(_omegaD * elapsedSeconds);

            double c1 = delta;
            double c2 = (initialVelocity + (DampingRatio * _omega0 * delta)) / _omegaD;

            double position = target + decay * (c1 * cos + c2 * sin);
            double velocity = decay * ((-DampingRatio * _omega0 * (c1 * cos + c2 * sin)) + (-c1 * _omegaD * sin + c2 * _omegaD * cos));

            bool hasSettled = Math.Abs(position - target) < 0.25 && Math.Abs(velocity) < 0.5 && elapsedSeconds >= FrequencyResponse * 0.7;

            return new SpringState(hasSettled ? target : position, velocity, hasSettled);
        }
        else
        {
            double decay = Math.Exp(-_omega0 * elapsedSeconds);
            double position = target + decay * (delta + (initialVelocity + _omega0 * delta) * elapsedSeconds);
            bool hasSettled = Math.Abs(position - target) < 0.25;
            return new SpringState(hasSettled ? target : position, 0.0, hasSettled);
        }
    }
}

public readonly record struct SpringState(double Position, double Velocity, bool HasSettled);
