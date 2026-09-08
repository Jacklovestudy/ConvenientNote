using ConvenientNote.Todos.Application;
using ConvenientNote.Todos.Domain;
using ConvenientNote.Todos.Infrastructure;
using ConvenientNote.Platform.Contracts;
using Xunit;
namespace ConvenientNote.Todos.Tests;
public sealed class CalendarContractTests
{
 [Fact] public async Task CalendarApiUsesCurrentWorkspaceAndPersistsCompletionAndRescheduling()
 {
  var path=Path.Combine(Path.GetTempPath(),$"todos-{Guid.NewGuid():N}.db");
  try{
   var context=new Context();var repo=new SqliteTodoRepository(path);var service=new TodoApplicationService(repo,context);
   var notifications=0;service.Changed+=(_,_)=>notifications++;
   var id=await service.CreateAsync("calendar task",new DateTime(2026,9,8,16,0,0));
   Assert.Equal(new DateTime(2026,9,8),Assert.Single(await service.ListAsync()).PlannedDate);
   await Task.WhenAll(service.SetCompletionAsync(id,true),service.RescheduleAsync(id,new DateTime(2026,9,9)));
   var persisted=Assert.Single(await repo.ListAsync(context.Id));Assert.True(persisted.IsCompleted);Assert.NotNull(persisted.CompletedAt);Assert.Equal(new DateTime(2026,9,9),persisted.PlannedDate);
   await service.RescheduleAsync(id,null);Assert.Null(Assert.Single(await service.ListAsync()).PlannedDate);
   await service.DeleteAsync(context.Id,new TodoId(id));Assert.Empty(await service.ListAsync());
   context.Id=Guid.NewGuid();Assert.Empty(await service.ListAsync());
   await Assert.ThrowsAsync<KeyNotFoundException>(()=>service.SetCompletionAsync(id,false));
   Assert.Equal(5,notifications);
  }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
 }
 [Fact] public async Task BrokenNotificationSubscriberDoesNotTurnCommittedSaveIntoFailure()
 {
  var path=Path.Combine(Path.GetTempPath(),$"todos-{Guid.NewGuid():N}.db");
  try{
   var service=new TodoApplicationService(new SqliteTodoRepository(path),new Context());
   service.Changed+=(_,_)=>throw new InvalidOperationException("Subscriber failed.");
   var notified=false;service.Changed+=(_,_)=>notified=true;
   await service.CreateAsync("saved",DateTime.Today);
   Assert.True(notified);Assert.Single(await service.ListAsync());
  }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
 }
 private sealed class Context:IWorkspaceContext
 {
  public Guid Id=Guid.NewGuid();
  public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken=default)=>Task.FromResult(new WorkspaceInfo(Id,"test"));
 }
}

