namespace ConvenientNote.Notes.Contracts;
/// <summary>Public read contract; consumers never receive domain entities.</summary>
public interface INotesApi
{
    Task<IReadOnlyList<NoteSummary>> ListAsync(CancellationToken cancellationToken = default);
}
public sealed record NoteSummary(Guid Id, string Title, string Preview, DateTimeOffset UpdatedAt);
