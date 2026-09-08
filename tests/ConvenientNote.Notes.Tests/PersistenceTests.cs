using ConvenientNote.Notes.Domain.Notes;
using ConvenientNote.Notes.Infrastructure;
using Xunit;
namespace ConvenientNote.Notes.Tests;
public sealed class PersistenceTests
{
 [Fact] public async Task LegacyRetryCannotOverwriteEditedNoteOrOtherWorkspace()
 {
  var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".db");
  try {
   var repository = new SqliteNotesRepository(path); var workspace = Guid.NewGuid();
   var note = new Note(NoteId.New(), "original", "", new(0,0), new(260,150), "#FFFFFF", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, legacyMetadata: new("red", true, new DateTime(2026,9,8), DateTimeOffset.UtcNow));
   await repository.ImportLegacyAsync(workspace, [note]);
   var stored = (await repository.ListAsync(workspace)).Single(); stored.Rename("edited");
   await repository.SaveAsync(workspace, stored);
   await repository.ImportLegacyAsync(workspace, [note]);
   Assert.Equal("edited", (await repository.ListAsync(workspace)).Single().Title);
   Assert.Empty(await repository.ListAsync(Guid.NewGuid()));
   Assert.Equal(note.LegacyMetadata, (await repository.ListAsync(workspace)).Single().LegacyMetadata);
  } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
 }
 [Fact] public async Task ReplacementPreservesTrashAndRejectsCollisionAtomically()
 {
  var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".db");
  try {
   var repository = new SqliteNotesRepository(path); var workspace = Guid.NewGuid();
   var active = Note.Create("active", "", new(0,0), new(260,150), "#FFFFFF", 0);
   var trash = Note.Create("trash", "", new(0,0), new(260,150), "#FFFFFF", 1); trash.MoveToTrash();
   await repository.ImportLegacyAsync(workspace, [active,trash]);
   var collision = new Note(trash.Id,"collision","",new(0,0),new(260,150),"#FFFFFF",0,trash.CreatedAt,trash.UpdatedAt);
   await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.ReplaceActiveAsync(workspace,[collision]));
   Assert.Equal(2,(await repository.ListAsync(workspace)).Count);
   await repository.ReplaceActiveAsync(workspace,[]);
   Assert.True((await repository.ListAsync(workspace)).Single().IsDeleted);
  } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
 }
}
