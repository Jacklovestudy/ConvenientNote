using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using NotesApplicationService = ConvenientNote.Notes.Application.NotesApplicationService;
using INotesRepository = ConvenientNote.Notes.Application.INotesRepository;
using ConvenientNote.Platform.Contracts;
using NewNote = ConvenientNote.Notes.Domain.Notes.Note;
using NewNoteId = ConvenientNote.Notes.Domain.Notes.NoteId;
namespace ConvenientNote.Tests.Compatibility;
/// <summary>Adapts legacy fault-injection fixtures to the new module ports. Never shipped with the app.</summary>
public sealed class NotesServiceFixture
{
 private readonly ConvenientNote.Application.Workspaces.WorkspaceApplicationService _legacy;
 public NotesApplicationService Service { get; }
 public NotesServiceFixture(IWorkspaceRepository repository)
 {
  _legacy = new(repository);
  Service = new(new LegacyNotesRepository(repository), new LegacyWorkspaceContext(_legacy));
 }
 public static implicit operator NotesApplicationService(NotesServiceFixture fixture)=>fixture.Service;
 public event EventHandler? WorkspaceChanged { add=>Service.WorkspaceChanged+=value;remove=>Service.WorkspaceChanged-=value; }
 public Task<WorkspaceSnapshot> GetOrCreateDefaultWorkspaceAsync(CancellationToken cancellationToken=default)=>_legacy.GetOrCreateDefaultWorkspaceAsync(cancellationToken);
 public Task<WorkspaceSnapshot> GetWorkspaceAsync(WorkspaceId id,CancellationToken cancellationToken=default)=>_legacy.GetWorkspaceAsync(id,cancellationToken);
 public Task<ConvenientNote.Application.Workspaces.NoteSnapshot> CreateNoteAsync(WorkspaceId id,double x,double y,string? title=null,string boardKey="testing",CancellationToken cancellationToken=default)=>_legacy.CreateNoteAsync(id,x,y,title,boardKey,cancellationToken);
 public Task<WorkspaceSnapshot> ReplaceActiveNotesAsync(WorkspaceId id,IReadOnlyCollection<NewNote> notes,CancellationToken cancellationToken=default)=>_legacy.ReplaceActiveNotesAsync(id,notes.Select(ToLegacy).ToArray(),cancellationToken);
 public static NewNote ToNew(Note n)=>new(new(n.Id.Value),n.Title,n.Content,new(n.Position.X,n.Position.Y),new(n.Size.Width,n.Size.Height),n.Color,n.ZIndex,n.CreatedAt,n.UpdatedAt,n.RichContent,n.NotebookId is {} id?new(id.Value):null,n.Tags,n.IsPinned,n.IsFavorite,n.IsDeleted,new(n.Priority,n.IsCompleted,n.PlannedDate,n.CompletedAt));
 public static Note ToLegacy(NewNote n)=>new(new(n.Id.Value),"testing",n.LegacyMetadata.Priority,n.Title,n.Content,new(n.Position.X,n.Position.Y),new(n.Size.Width,n.Size.Height),n.Color,n.ZIndex,n.LegacyMetadata.IsCompleted,n.CreatedAt,n.UpdatedAt,n.RichContent,n.NotebookId is {} id?new(id.Value):null,n.Tags,n.IsPinned,n.IsFavorite,n.IsDeleted,n.LegacyMetadata.PlannedDate,n.LegacyMetadata.CompletedAt);
 public static ConvenientNote.Notes.Application.NoteSnapshot ToSnapshot(ConvenientNote.Application.Workspaces.NoteSnapshot n)=>new(new(n.Id.Value),n.BoardKey,n.Priority,n.Title,n.Content,n.X,n.Y,n.Width,n.Height,n.Color,n.ZIndex,n.IsCompleted,n.RichContent,n.NotebookId is {} id?new(id.Value):null,n.Tags,n.IsPinned,n.IsFavorite,n.IsDeleted,n.CreatedAt,n.UpdatedAt,n.PlannedDate,n.CompletedAt);
 private sealed class LegacyWorkspaceContext(ConvenientNote.Application.Workspaces.WorkspaceApplicationService service):IWorkspaceContext
 { public async Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken=default){var w=await service.GetOrCreateDefaultWorkspaceAsync(cancellationToken);return new(w.Id.Value,w.Name);} }
 private sealed class LegacyNotesRepository(IWorkspaceRepository repository):INotesRepository
 {
  public async Task<IReadOnlyList<NewNote>> ListAsync(Guid workspaceId,CancellationToken cancellationToken=default)=> (await repository.GetAsync(new(workspaceId),cancellationToken))!.Notes.Where(n=>n.BoardKey=="testing").Select(ToNew).ToList();
  public Task SaveAsync(Guid workspaceId,NewNote note,CancellationToken cancellationToken=default)=>SaveManyAsync(workspaceId,[note],cancellationToken);
  public async Task SaveManyAsync(Guid workspaceId,IReadOnlyCollection<NewNote> notes,CancellationToken cancellationToken=default)
  {var w=(await repository.GetAsync(new(workspaceId),cancellationToken))!;var ids=notes.Select(n=>n.Id.Value).ToHashSet();var replacement=new Workspace(w.Id,w.Name,w.CreatedAt,w.UpdatedAt,w.Notes.Where(n=>!ids.Contains(n.Id.Value)).Concat(notes.Select(ToLegacy)));await repository.SaveAsync(replacement,cancellationToken);}
  public async Task DeleteAsync(Guid workspaceId,NewNoteId noteId,CancellationToken cancellationToken=default){var w=(await repository.GetAsync(new(workspaceId),cancellationToken))!;w.RemoveNote(new(noteId.Value));await repository.SaveAsync(w,cancellationToken);}
  public Task ReplaceActiveAsync(Guid workspaceId,IReadOnlyCollection<NewNote> notes,CancellationToken cancellationToken=default)=>repository.ReplaceActiveNotesAsync(new(workspaceId),notes.Select(ToLegacy).ToArray(),cancellationToken);
  public Task ImportLegacyAsync(Guid workspaceId,IReadOnlyCollection<NewNote> notes,CancellationToken cancellationToken=default)=>throw new NotSupportedException("Legacy fixture only; real import tests use SqliteNotesRepository.");
 }
}
