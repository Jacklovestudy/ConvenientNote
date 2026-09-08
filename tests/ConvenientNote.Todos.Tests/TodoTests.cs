using ConvenientNote.Todos.Domain;
using ConvenientNote.Todos.Infrastructure;
using Xunit;
namespace ConvenientNote.Todos.Tests;
public sealed class TodoTests
{
 [Fact] public void CompletionAndDatesHaveConsistentState()
 {
  var todo = TodoItem.Create(" task ", 12, 30);
  todo.Reschedule(new DateTime(2026,9,8,12,30,0));
  Assert.Equal(new DateTime(2026,9,8),todo.PlannedDate);
  todo.SetCompletion(true); var completed = todo.CompletedAt;
  Assert.NotNull(completed); todo.SetCompletion(true); Assert.Equal(completed,todo.CompletedAt);
  todo.SetCompletion(false); Assert.Null(todo.CompletedAt);
  Assert.Throws<ArgumentOutOfRangeException>(()=>todo.SetPriority("purple"));
 }
 [Fact] public async Task ImportsAreInsertOnlyAndWorkspacesRemainIsolated()
 {
  var path=Path.Combine(Path.GetTempPath(),$"todos-{Guid.NewGuid():N}.db");
  try {
   var repo=new SqliteTodoRepository(path); var a=Guid.NewGuid(); var b=Guid.NewGuid();
   var first=TodoItem.Create("original",0,0); var second=TodoItem.Create("other",0,0);
   await repo.ImportLegacyAsync(a,[first]); await repo.ImportLegacyAsync(b,[second]);
   first.Rename("edited"); await repo.SaveAsync(a,[first]);
   var original=TodoItem.Restore(first.Id,"original","","blue",0,0,280,220,"#fff",0,false,false,first.CreatedAt,first.CreatedAt,null,null);
   await repo.ImportLegacyAsync(a,[original]);
   Assert.Equal("edited",Assert.Single(await repo.ListAsync(a)).Title);
   Assert.Equal("other",Assert.Single(await repo.ListAsync(b)).Title);
   Assert.Empty(await repo.ListAsync(Guid.NewGuid()));
  } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
 }
}
