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
}
