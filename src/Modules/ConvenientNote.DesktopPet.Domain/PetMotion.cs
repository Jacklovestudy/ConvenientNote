namespace ConvenientNote.DesktopPet.Domain;

public enum PetAction { Ride, Boost, Brake, React, Drag, Sleep }

public sealed class PetMotion
{
    private double _idle;
    private bool _turn;
    private double _brakingSpeed = 22;
    public PetAction Action { get; private set; } = PetAction.Ride;
    public double Age { get; private set; }
    public int Direction { get; private set; } = 1;
    public double Speed => Action switch
    {
        PetAction.Ride => 22,
        PetAction.Boost => 70,
        PetAction.Brake => _brakingSpeed * Math.Max(0, 1 - Age / .5),
        _ => 0
    };

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Age += seconds;
        if (Action is PetAction.Drag or PetAction.Sleep) return;
        _idle += seconds;
        if (Action == PetAction.Boost && Age >= 2.5) Brake();
        else if (Action == PetAction.Brake && Age >= .5)
        {
            if (_turn) Direction *= -1;
            _turn = false;
            Set(PetAction.Ride);
        }
        else if (Action == PetAction.React && Age >= 1.4) Set(PetAction.Ride);
        if (_idle >= 90) Set(PetAction.Sleep);
    }

    public void React() => Interact(PetAction.React);
    public void Boost() => Interact(PetAction.Boost);
    public void Sleep() => Interact(PetAction.Sleep);
    public void Ride() => Interact(PetAction.Ride);
    public void BeginDrag() => Interact(PetAction.Drag);
    public void Release() { _idle = 0; _turn = false; _brakingSpeed = 0; Set(PetAction.Brake); }
    public void TurnAtEdge()
    {
        if (Action is not (PetAction.Ride or PetAction.Boost)) return;
        Brake();
        _turn = true;
    }
    public void Brake() { _brakingSpeed = Speed; Set(PetAction.Brake); }
    private void Interact(PetAction action) { _idle = 0; _turn = false; Set(action); }
    private void Set(PetAction action) { Action = action; Age = 0; }
}
