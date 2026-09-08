namespace ConvenientNote.Views;

internal sealed class ManualSaveRequestGate
{
    private bool _saveInProgress;

    public bool TryBegin(bool isRepeat)
    {
        if (isRepeat || _saveInProgress)
        {
            return false;
        }

        _saveInProgress = true;
        return true;
    }

    public void Complete()
    {
        _saveInProgress = false;
    }
}
