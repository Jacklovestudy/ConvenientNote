using ConvenientNote.Domain;

namespace ConvenientNote.Domain.Notes;

public readonly record struct NoteSize
{
    public const double MinWidth = 120;
    public const double MinHeight = 80;

    public NoteSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            throw new DomainException("Note size must be finite.");
        }

        if (width < MinWidth || height < MinHeight)
        {
            throw new DomainException($"Note size must be at least {MinWidth}x{MinHeight}.");
        }

        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }
}
