using CodexIsland.Core.Models;

namespace CodexIsland.Core.Signals;

public sealed class CompletionTransitionDetector
{
    private ProjectSignal? _lastSignal;

    public bool ShouldBounce(ProjectSignal nextSignal)
    {
        var shouldBounce = nextSignal == ProjectSignal.Completed && _lastSignal != ProjectSignal.Completed;
        _lastSignal = nextSignal;
        return shouldBounce;
    }
}
