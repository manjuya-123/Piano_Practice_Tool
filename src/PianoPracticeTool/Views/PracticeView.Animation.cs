using PianoPracticeTool.Services.Practice;

namespace PianoPracticeTool.Views;

public sealed partial class PracticeView
{
    public void SetAnimationPosition(PracticeRunState state, double currentBeat)
    {
        PianoRoll.CurrentBeat = state == PracticeRunState.Ready
            ? null
            : currentBeat;
    }
}
