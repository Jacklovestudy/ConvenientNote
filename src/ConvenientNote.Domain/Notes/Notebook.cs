using ConvenientNote.Domain;

namespace ConvenientNote.Domain.Notes;

public sealed class Notebook
{
    public Notebook(NotebookId id, string name, bool isSystem = false)
    {
        Id = id;
        Name = NormalizeName(name);
        IsSystem = isSystem;
    }

    public NotebookId Id { get; }

    public string Name { get; private set; }

    public bool IsSystem { get; }

    public void Rename(string name) => Name = NormalizeName(name);

    private static string NormalizeName(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "未分类" : name.Trim();
        if (normalized.Length > 40)
        {
            throw new DomainException("Notebook name cannot exceed 40 characters.");
        }

        return normalized;
    }
}
