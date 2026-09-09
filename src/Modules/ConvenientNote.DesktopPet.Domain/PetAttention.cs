namespace ConvenientNote.DesktopPet.Domain;

/// <summary>Contact timing and stroking rules, independent of screen coordinates.</summary>
public sealed class PetAttention
{
    private double _headTime, _stroke, _pettingTime;
    private bool _woke;
    public bool IsHovering { get; private set; }
    public bool IsPetting => _pettingTime > 0;

    public bool Advance(double seconds, bool onPet, bool onHead, double travel, bool sleeping)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!double.IsFinite(travel) || travel < 0) throw new ArgumentOutOfRangeException(nameof(travel));
        IsHovering = onPet;
        if (!onPet || !onHead)
        {
            _headTime = _stroke = _pettingTime = 0;
            _woke = false;
            return false;
        }
        _headTime += seconds;
        _stroke = Math.Max(0, _stroke - seconds * 18) + Math.Min(travel, 20);
        _pettingTime = Math.Max(0, _pettingTime - seconds);
        if (_stroke >= 28) { _pettingTime = .8; _stroke = 0; }
        if (sleeping && !_woke && _headTime >= .7) { _woke = true; return true; }
        return false;
    }
}
