using ConvenientNote.Domain;

namespace ConvenientNote.Domain.Notes;

public readonly record struct NotePosition
{
    public NotePosition(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new DomainException("Note position must be finite.");
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}
