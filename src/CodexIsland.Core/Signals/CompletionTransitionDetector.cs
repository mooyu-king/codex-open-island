using CodexIsland.Core.Models;

namespace CodexIsland.Core.Signals;

public sealed class CompletionTransitionDetector
{
    private ProjectSignal? _lastSignal;
    private bool _initialized;

    /// Returns true when the signal transitions into a state that warrants
    /// a persistent bounce: Completed or Permission.
    public bool ShouldStartPersistentBounce(ProjectSignal nextSignal)
    {
        if (!_initialized)
        {
            _initialized = true;
            _lastSignal = nextSignal;
            return false;
        }

        var shouldBounce = (nextSignal is ProjectSignal.Completed or ProjectSignal.Permission)
                           && _lastSignal != nextSignal;
        _lastSignal = nextSignal;
        return shouldBounce;
    }

    /// After the user acknowledges the bounce, call this so the detector
    /// knows the current signal has been seen.
    public void Acknowledge(ProjectSignal currentSignal)
    {
        _initialized = true;
        _lastSignal = currentSignal;
    }
}
