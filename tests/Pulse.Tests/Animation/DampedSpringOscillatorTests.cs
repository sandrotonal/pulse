using Pulse.Core.Animation;

namespace Pulse.Tests.Animation;

public sealed class DampedSpringOscillatorTests
{
    [Fact]
    public void InitialStateMatchesStartPosition()
    {
        var spring = new DampedSpringOscillator(dampingRatio: 0.82, responseSeconds: 0.35);
        var state = spring.Evaluate(elapsedSeconds: 0.0, start: 100.0, target: 200.0);
        Assert.Equal(100.0, state.Position, precision: 2);
        Assert.False(state.HasSettled);
    }

    [Fact]
    public void SettlesNearTargetAfterSufficientTime()
    {
        var spring = new DampedSpringOscillator(dampingRatio: 0.82, responseSeconds: 0.35);
        var state = spring.Evaluate(elapsedSeconds: 1.0, start: 100.0, target: 200.0);
        Assert.True(Math.Abs(state.Position - 200.0) < 0.2);
        Assert.True(state.HasSettled);
    }

    [Fact]
    public void ProducesFluidOvershootConsistentWithDampingRatio()
    {
        var spring = new DampedSpringOscillator(dampingRatio: 0.82, responseSeconds: 0.35);
        bool hasOvershot = false;
        for (double t = 0.05; t <= 0.6; t += 0.02)
        {
            var state = spring.Evaluate(t, start: 0.0, target: 100.0);
            if (state.Position > 100.0)
            {
                hasOvershot = true;
                break;
            }
        }
        Assert.True(hasOvershot);
    }

    [Fact]
    public void MidFlightRetargetingPreservesMomentumBeforeReversing()
    {
        var spring = new DampedSpringOscillator(dampingRatio: 0.82, responseSeconds: 0.34);

        // Expand partway from 216 -> 384 and capture mid-flight state.
        var mid = spring.Evaluate(elapsedSeconds: 0.08, start: 216.0, target: 384.0);
        Assert.True(mid.Velocity > 100.0);

        // Retarget mid-flight back to 216 while carrying current velocity.
        var justAfterRetarget = spring.Evaluate(
            elapsedSeconds: 0.012,
            start: mid.Position,
            target: 216.0,
            initialVelocity: mid.Velocity);

        // Because positive momentum is preserved, position continues forward briefly before reversing.
        Assert.True(justAfterRetarget.Position > mid.Position);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.3)]
    public void CriticallyAndOverdampedSpringsReportNonZeroMidFlightVelocity(double dampingRatio)
    {
        var spring = new DampedSpringOscillator(dampingRatio: dampingRatio, responseSeconds: 0.34);
        var mid = spring.Evaluate(elapsedSeconds: 0.06, start: 216.0, target: 384.0);

        Assert.True(mid.Position > 216.0 && mid.Position < 384.0);
        Assert.True(mid.Velocity > 10.0);
        Assert.False(mid.HasSettled);

        var end = spring.Evaluate(elapsedSeconds: 1.5, start: 216.0, target: 384.0);
        Assert.True(end.HasSettled);
        Assert.Equal(384.0, end.Position, precision: 3);
    }

    [Fact]
    public void NormalizedToleranceDoesNotSnapPrematurelyOnUnitRange()
    {
        var spring = new DampedSpringOscillator(dampingRatio: 0.9, responseSeconds: 0.30);

        // At t = 0.22s on a 0 -> 1 opacity transition, pixel tolerance (0.25) would wrongly settle,
        // whereas normalized tolerance (0.004) keeps animating smoothly.
        var state = spring.Evaluate(
            elapsedSeconds: 0.22,
            start: 0.0,
            target: 1.0,
            initialVelocity: 0.0,
            positionTolerance: 0.004,
            velocityTolerance: 0.02);

        Assert.False(state.HasSettled);
        Assert.InRange(state.Position, 0.85, 1.05);
    }

    [Theory]
    [InlineData(0.0, 0.0005)]
    [InlineData(-0.1, 0.0005)]
    [InlineData(0.002777, 0.002777)]
    [InlineData(0.006944, 0.006944)]
    [InlineData(2.5, 0.033)]
    public void ClampFrameDeltaSecondsBoundsExtremeSteps(double rawDt, double expectedDt)
    {
        Assert.Equal(expectedDt, DampedSpringOscillator.ClampFrameDeltaSeconds(rawDt), precision: 6);
    }
}
