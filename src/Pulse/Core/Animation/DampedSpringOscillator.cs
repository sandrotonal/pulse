namespace Pulse.Core.Animation;

public sealed class DampedSpringOscillator
{
    public const double MinFrameDeltaSeconds = 0.0005;
    public const double MaxFrameDeltaSeconds = 0.033;
    public const double DefaultPositionTolerance = 0.25;
    public const double DefaultVelocityTolerance = 0.5;
    public const double NormalizedPositionTolerance = 0.004;
    public const double NormalizedVelocityTolerance = 0.02;

    public double DampingRatio { get; }
    public double FrequencyResponse { get; }

    private readonly double _omega0;
    private readonly double _omegaD;
    private readonly double _overdampedGamma;

    public DampedSpringOscillator(double dampingRatio = 0.82, double responseSeconds = 0.35)
    {
        DampingRatio = Math.Clamp(dampingRatio, 0.01, 2.0);
        FrequencyResponse = Math.Max(0.01, responseSeconds);

        _omega0 = (2.0 * Math.PI) / FrequencyResponse;

        if (DampingRatio < 1.0 - 1e-4)
        {
            _omegaD = _omega0 * Math.Sqrt(1.0 - (DampingRatio * DampingRatio));
            _overdampedGamma = 0.0;
        }
        else if (DampingRatio > 1.0 + 1e-4)
        {
            _omegaD = 0.0;
            _overdampedGamma = _omega0 * Math.Sqrt((DampingRatio * DampingRatio) - 1.0);
        }
        else
        {
            _omegaD = 0.0;
            _overdampedGamma = 0.0;
        }
    }

    public static double ClampFrameDeltaSeconds(double rawDeltaSeconds)
    {
        if (!double.IsFinite(rawDeltaSeconds))
        {
            return MinFrameDeltaSeconds;
        }

        return Math.Clamp(rawDeltaSeconds, MinFrameDeltaSeconds, MaxFrameDeltaSeconds);
    }

    public SpringState Evaluate(
        double elapsedSeconds,
        double start,
        double target,
        double initialVelocity = 0.0,
        double positionTolerance = DefaultPositionTolerance,
        double velocityTolerance = DefaultVelocityTolerance)
    {
        double delta = start - target;

        if (Math.Abs(delta) < Math.Min(0.0001, positionTolerance * 0.01) &&
            Math.Abs(initialVelocity) < Math.Min(0.0001, velocityTolerance * 0.01))
        {
            return new SpringState(target, 0.0, true);
        }

        if (elapsedSeconds <= 0.0)
        {
            return new SpringState(start, initialVelocity, false);
        }

        double position;
        double velocity;

        if (_omegaD > 0.0)
        {
            double decay = Math.Exp(-DampingRatio * _omega0 * elapsedSeconds);
            double cos = Math.Cos(_omegaD * elapsedSeconds);
            double sin = Math.Sin(_omegaD * elapsedSeconds);

            double c1 = delta;
            double c2 = (initialVelocity + (DampingRatio * _omega0 * delta)) / _omegaD;

            double envelope = (c1 * cos) + (c2 * sin);
            position = target + (decay * envelope);
            velocity = decay * ((-DampingRatio * _omega0 * envelope) + ((-c1 * _omegaD * sin) + (c2 * _omegaD * cos)));
        }
        else if (_overdampedGamma > 0.0)
        {
            double r1 = (-DampingRatio * _omega0) + _overdampedGamma;
            double r2 = (-DampingRatio * _omega0) - _overdampedGamma;

            double c1 = (initialVelocity - (r2 * delta)) / (r1 - r2);
            double c2 = delta - c1;

            double e1 = Math.Exp(r1 * elapsedSeconds);
            double e2 = Math.Exp(r2 * elapsedSeconds);

            position = target + (c1 * e1) + (c2 * e2);
            velocity = (c1 * r1 * e1) + (c2 * r2 * e2);
        }
        else
        {
            double decay = Math.Exp(-_omega0 * elapsedSeconds);
            double c1 = delta;
            double c2 = initialVelocity + (_omega0 * delta);
            double poly = c1 + (c2 * elapsedSeconds);

            position = target + (decay * poly);
            velocity = decay * (c2 - (_omega0 * poly));
        }

        bool hasSettled =
            Math.Abs(position - target) <= positionTolerance &&
            Math.Abs(velocity) <= velocityTolerance &&
            elapsedSeconds >= FrequencyResponse * 0.7;

        return new SpringState(hasSettled ? target : position, hasSettled ? 0.0 : velocity, hasSettled);
    }
}

public readonly record struct SpringState(double Position, double Velocity, bool HasSettled);
