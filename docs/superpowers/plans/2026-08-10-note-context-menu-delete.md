# Note Context Menu Delete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an immediate permanent “删除便签” action to every note context menu while preserving the native text-editing actions inside title and content fields.

**Architecture:** `TodoBoardControl` owns context-menu presentation and forwards the selected `CanvasTodoViewModel` to a new asynchronous deletion entry point on `TodoBoardViewModel`. The ViewModel reuses `WorkspaceApplicationService.DeleteNoteAsync` and reloads the workspace only after persistence succeeds, so collection, board dimensions, summary, empty state, and arrange availability stay consistent.

**Tech Stack:** C# 14, .NET 10, WPF XAML, Prism MVVM, xUnit

## Global Constraints

- Deletion is immediate and permanent; do not show a confirmation dialog.
- Right-clicking a note background must expose “删除便签”.
- Title and content text boxes must retain Cut, Copy, Paste, and Select All, followed by “删除便签”.
- A failed persistence operation must leave the note visible and only write to the existing debug log.
- Reuse `WorkspaceApplicationService.DeleteNoteAsync`; do not add domain or database deletion behavior.

---

### Task 1: ViewModel deletion behavior

**Files:**
- Create: `tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`
- Create: `tests/ConvenientNote.Tests/ViewModels/TodoBoardViewModelTests.cs`
- Modify: `ConvenientNote.csproj`
- Modify: `ConvenientNote.slnx`
- Modify: `ViewModels/TodoBoardViewModel.cs:210-225`

**Interfaces:**
- Consumes: `WorkspaceApplicationService.DeleteNoteAsync(WorkspaceId, NoteId, CancellationToken)`
- Produces: `Task TodoBoardViewModel.DeleteTodoAsync(CanvasTodoViewModel todo)`

- [ ] **Step 1: Create the test project without leaking test sources into the WPF app**

Add these exclusions to the root project beside the existing `src` exclusions:

```xml
<Compile Remove="tests\**\*.cs" />
<EmbeddedResource Remove="tests\**" />
<None Remove="tests\**" />
<Page Remove="tests\**" />
```

Create `tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\ConvenientNote.csproj" />
  </ItemGroup>
</Project>
```

Run:

```powershell
dotnet sln ConvenientNote.slnx add tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj
```

- [ ] **Step 2: Write the failing ViewModel test**

Create `TodoBoardViewModelTests.cs` with a real `Workspace`, real `WorkspaceApplicationService`, and an in-memory `IWorkspaceRepository`. The test must load one active note, delete it through the wished-for ViewModel API, and assert the user-visible state:

```csharp
[Fact]
public async Task DeleteTodoAsync_RemovesPersistedTodoAndRefreshesBoardState()
{
    var workspace = Workspace.Create("测试工作区");
    workspace.AddNote(
        TodoBoardKeys.DayTodo,
        "待删除",
        string.Empty,
        new NotePosition(32, 32),
        new NoteSize(260, 150),
        "#FFF8B8");
    var repository = new InMemoryWorkspaceRepository(workspace);
    var viewModel = new DayTodoViewModel(
        new WorkspaceApplicationService(repository),
        new OpenMeteoWeatherService());

    viewModel.OnNavigatedTo(null!);
    await WaitUntilAsync(() => viewModel.TodoItems.Count == 1);

    await viewModel.DeleteTodoAsync(viewModel.TodoItems.Single());

    Assert.Empty(viewModel.TodoItems);
    Assert.Equal(Visibility.Visible, viewModel.EmptyStateVisibility);
    Assert.Equal(1800, viewModel.BoardWidth);
    Assert.Equal(1100, viewModel.BoardHeight);
    Assert.False(viewModel.CanArrangeTodos);
    Assert.Empty((await repository.GetAsync(workspace.Id))!.Notes);
}
```

The test utility `WaitUntilAsync` polls for at most two seconds. `InMemoryWorkspaceRepository` implements all four `IWorkspaceRepository` members and stores the real `Workspace` instance passed to `SaveAsync`; do not mock `DeleteNoteAsync` or assert a mock call.

- [ ] **Step 3: Run the test and verify RED**

Run:

```powershell
dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --filter DeleteTodoAsync_RemovesPersistedTodoAndRefreshesBoardState
```

Expected: compilation fails because `TodoBoardViewModel.DeleteTodoAsync` does not exist. This is the production change that the test protects.

- [ ] **Step 4: Implement the minimal ViewModel method**

Add this public method beside the existing commit methods:

```csharp
public async Task DeleteTodoAsync(CanvasTodoViewModel todo)
{
    if (_currentWorkspaceId is not { } workspaceId)
    {
        return;
    }

    try
    {
        await _workspaceApplicationService.DeleteNoteAsync(workspaceId, todo.Id);
        await LoadWorkspaceAsync();
    }
    catch (Exception ex)
    {
        Debug.WriteLine(ex);
    }
}
```

Reload only after `DeleteNoteAsync` succeeds. `LoadWorkspaceAsync` already rebuilds `TodoItems` and refreshes all derived board state.

- [ ] **Step 5: Run tests and verify GREEN**

Run:

```powershell
dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj
```

Expected: all tests pass with no errors or warnings.

- [ ] **Step 6: Commit the tested ViewModel behavior**

```powershell
git add ConvenientNote.csproj ConvenientNote.slnx ViewModels/TodoBoardViewModel.cs tests/ConvenientNote.Tests
git commit -m "feat: add note deletion behavior"
```

---

### Task 2: Context menus and UI forwarding

**Files:**
- Modify: `Views/TodoBoardControl.xaml:328-407`
- Modify: `Views/TodoBoardControl.xaml.cs:140-175`

**Interfaces:**
- Consumes: `Task TodoBoardViewModel.DeleteTodoAsync(CanvasTodoViewModel todo)`
- Produces: `TodoDeleteMenuItem_Click(object sender, RoutedEventArgs e)` and context menus for card, title, and content

- [ ] **Step 1: Add the card-background context menu**

Inside the note `Border`, add a `ContextMenu` whose data context follows the placement target:

```xml
<Border.ContextMenu>
    <ContextMenu DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}">
        <MenuItem Header="删除便签"
                  Foreground="#B42318"
                  Click="TodoDeleteMenuItem_Click">
            <MenuItem.Icon>
                <materialDesign:PackIcon Kind="DeleteOutline" Foreground="#B42318" />
            </MenuItem.Icon>
        </MenuItem>
    </ContextMenu>
</Border.ContextMenu>
```

- [ ] **Step 2: Preserve text editing commands and append deletion**

Give both the title and content `TextBox` an inline `ContextMenu` with this exact order:

```xml
<ContextMenu DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}">
    <MenuItem Header="剪切" Command="ApplicationCommands.Cut" InputGestureText="Ctrl+X" />
    <MenuItem Header="复制" Command="ApplicationCommands.Copy" InputGestureText="Ctrl+C" />
    <MenuItem Header="粘贴" Command="ApplicationCommands.Paste" InputGestureText="Ctrl+V" />
    <MenuItem Header="全选" Command="ApplicationCommands.SelectAll" InputGestureText="Ctrl+A" />
    <Separator />
    <MenuItem Header="删除便签"
              Foreground="#B42318"
              Click="TodoDeleteMenuItem_Click">
        <MenuItem.Icon>
            <materialDesign:PackIcon Kind="DeleteOutline" Foreground="#B42318" />
        </MenuItem.Icon>
    </MenuItem>
</ContextMenu>
```

The explicit text commands preserve the menu shown in the user’s screenshot rather than replacing it with a delete-only menu.

- [ ] **Step 3: Forward the selected note to the ViewModel**

Add the handler near the other note editing handlers:

```csharp
private async void TodoDeleteMenuItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: CanvasTodoViewModel todo } &&
        DataContext is TodoBoardViewModel viewModel)
    {
        await viewModel.DeleteTodoAsync(todo);
    }

    e.Handled = true;
}
```

- [ ] **Step 4: Build and run the complete automated suite**

Run:

```powershell
dotnet test ConvenientNote.slnx
dotnet build ConvenientNote.slnx --no-restore
```

Expected: tests pass and the WPF project builds with zero errors and zero warnings.

- [ ] **Step 5: Perform the focused interaction check**

Launch the app and verify:

1. Right-click note background: “删除便签” appears and deletes immediately.
2. Right-click title: Cut, Copy, Paste, Select All, separator, and “删除便签” appear.
3. Right-click content: the same editing commands and delete action appear.
4. Deleting the last visible note shows the existing empty state.
5. No confirmation dialog appears.

- [ ] **Step 6: Commit the UI**

```powershell
git add Views/TodoBoardControl.xaml Views/TodoBoardControl.xaml.cs
git commit -m "feat: add delete action to note context menus"
```
