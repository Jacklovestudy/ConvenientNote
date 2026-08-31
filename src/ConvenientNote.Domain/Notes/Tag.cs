using ConvenientNote.Domain;

namespace ConvenientNote.Domain.Notes;

public sealed record Tag
{
    public Tag(string name)
    {
        Name = Normalize(name);
    }

    public string Name { get; }

    private static string Normalize(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 24)
        {
            throw new DomainException("Tag name must contain between 1 and 24 characters.");
        }

        return normalized;
    }
}
