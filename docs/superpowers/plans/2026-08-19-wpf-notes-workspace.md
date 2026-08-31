# WPF 笔记工作区实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将“待测试”替换为原生 WPF 笔记工作区，提供便签墙、富文本大编辑器、本地图片、自动保存、笔记本、标签、搜索、置顶、收藏和回收站语义。

**Architecture:** 保留现有 `Note` 聚合和 SQLite 工作区仓储，在同一聚合中补充笔记元数据，新增笔记本、标签及关联表。WPF 表示层使用独立的笔记墙与富文本编辑器控件，由 `NotesViewModel` 管理墙/编辑器状态；富文本文档和图片操作分别由专用服务封装。

**Tech Stack:** .NET 10、WPF、Prism、MaterialDesignThemes、EF Core SQLite、xUnit

**Spec:** `docs/superpowers/specs/2026-08-19-wpf-notes-workspace-design.md`

## Global Constraints

- 仅使用原生 WPF，不引入 WebView2 或 JavaScript 编辑器。
- 现有 `BoardKey = "testing"` 数据必须保留并显示为“未分类”笔记。
- 图片保存在 `%LocalAppData%/ConvenientNote/Media/<note-id>/`，数据库和文档只保存相对路径。
- 富文本解析不得加载任意外部 XAML 类型；持久化格式限定为应用定义的 JSON 文档模型。
- 保存必须串行且可重试，旧保存结果不得覆盖更新内容。
- 未获得用户明确授权，不执行 `git commit`、`git push` 或创建拉取请求。

---

### Task 1: 扩展笔记领域模型

**Files:**
- Create: `src/ConvenientNote.Domain/Notes/NotebookId.cs`
- Create: `src/ConvenientNote.Domain/Notes/Notebook.cs`
- Create: `src/ConvenientNote.Domain/Notes/Tag.cs`
- Modify: `src/ConvenientNote.Domain/Notes/Note.cs`
- Modify: `src/ConvenientNote.Domain/Workspaces/Workspace.cs`
- Test: `tests/ConvenientNote.Tests/Domain/NoteMetadataTests.cs`

**Interfaces:**
- Produces: `NotebookId`, `Notebook`, `Tag`, `Note.SetNotebook`, `Note.SetTags`, `Note.SetPinned`, `Note.SetFavorite`, `Note.UpdateRichContent`, `Note.MoveToTrash`, `Note.Restore`。

- [ ] **Step 1: 编写失败的领域测试**

```csharp
[Fact]
public void Note_metadata_changes_are_normalized_and_timestamped()
{
    var note = CreateNote();
    var notebookId = NotebookId.New();

    note.SetNotebook(notebookId);
    note.SetTags([" 工作 ", "灵感", "工作"]);
    note.SetPinned(true);
    note.SetFavorite(true);
    note.UpdateRichContent("{\"version\":1,\"blocks\":[]}", "正文");

    Assert.Equal(notebookId, note.NotebookId);
    Assert.Equal(["工作", "灵感"], note.Tags);
    Assert.True(note.IsPinned);
    Assert.True(note.IsFavorite);
    Assert.Equal("正文", note.Content);
}
```

- [ ] **Step 2: 运行测试并确认因接口不存在而失败**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --filter NoteMetadataTests`

- [ ] **Step 3: 实现值对象、元数据状态和约束**

```csharp
public void SetTags(IEnumerable<string> tags)
{
    _tags = tags.Select(static tag => tag.Trim())
        .Where(static tag => tag.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(20)
        .ToList();
    Touch();
}
```

- [ ] **Step 4: 运行领域测试与全部现有测试**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`

- [ ] **Step 5: 检查工作区，不提交**

Run: `git status --short`

### Task 2: 扩展快照、应用服务和 SQLite 架构

**Files:**
- Modify: `src/ConvenientNote.Application/Workspaces/NoteSnapshot.cs`
- Create: `src/ConvenientNote.Application/Workspaces/NotebookSnapshot.cs`
- Modify: `src/ConvenientNote.Application/Workspaces/WorkspaceSnapshot.cs`
- Modify: `src/ConvenientNote.Application/Workspaces/WorkspaceApplicationService.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/Entities/NoteEntity.cs`
- Create: `src/ConvenientNote.Infrastructure/Persistence/Entities/NotebookEntity.cs`
- Create: `src/ConvenientNote.Infrastructure/Persistence/Entities/TagEntity.cs`
- Create: `src/ConvenientNote.Infrastructure/Persistence/Entities/NoteTagEntity.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/ConvenientNoteDbContext.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/SqliteWorkspaceRepository.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/JsonWorkspaceRepository.cs`
- Test: `tests/ConvenientNote.Tests/Infrastructure/NotesSchemaUpgradeTests.cs`

**Interfaces:**
- Consumes: Task 1 领域属性。
- Produces: `UpdateRichNoteAsync`, `SetNoteNotebookAsync`, `SetNoteTagsAsync`, `SetNotePinnedAsync`, `SetNoteFavoriteAsync`, `MoveNoteToTrashAsync`, `RestoreNoteAsync`。

- [ ] **Step 1: 编写旧数据库升级失败测试**

```csharp
[Fact]
public async Task Existing_testing_notes_survive_notes_schema_upgrade()
{
    var path = await CreateLegacyDatabaseAsync(boardKey: "testing", title: "旧笔记");
    var repository = new SqliteWorkspaceRepository(path);

    var workspace = Assert.Single(await repository.ListAsync());

    var note = Assert.Single(workspace.Notes);
    Assert.Equal("旧笔记", note.Title);
    Assert.Equal("testing", note.BoardKey);
    Assert.NotNull(note.NotebookId);
}
```

- [ ] **Step 2: 运行升级测试并确认失败**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --filter NotesSchemaUpgradeTests`

- [ ] **Step 3: 增加实体映射和幂等升级 SQL**

```sql
ALTER TABLE "Notes" ADD COLUMN "RichContent" TEXT NOT NULL DEFAULT '';
ALTER TABLE "Notes" ADD COLUMN "NotebookId" TEXT NULL;
ALTER TABLE "Notes" ADD COLUMN "IsPinned" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Notes" ADD COLUMN "IsFavorite" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Notes" ADD COLUMN "IsDeleted" INTEGER NOT NULL DEFAULT 0;
```

每个 `ALTER TABLE` 前通过 `PRAGMA table_info` 检查列；系统“未分类”笔记本使用稳定 ID，避免重复启动创建多份。

- [ ] **Step 4: 实现应用服务元数据和富文本更新入口**

```csharp
public async Task UpdateRichNoteAsync(
    WorkspaceId workspaceId,
    NoteId noteId,
    string richContent,
    string plainText,
    CancellationToken cancellationToken = default)
```

- [ ] **Step 5: 运行升级测试、仓储测试和全部测试**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`

- [ ] **Step 6: 检查工作区，不提交**

Run: `git status --short`

### Task 3: 实现安全富文本文档与本地媒体服务

**Files:**
- Create: `Services/RichText/RichTextDocumentModel.cs`
- Create: `Services/RichText/RichTextDocumentService.cs`
- Create: `Services/NoteMediaService.cs`
- Test: `tests/ConvenientNote.Tests/Services/RichTextDocumentServiceTests.cs`
- Test: `tests/ConvenientNote.Tests/Services/NoteMediaServiceTests.cs`

**Interfaces:**
- Produces: `RichTextDocumentService.Load(string?, string)`, `Save(FlowDocument)`, `ExtractPlainText(FlowDocument)`；`NoteMediaService.ImportAsync(NoteId, string)`、`DeleteOrphansAsync(NoteId, IReadOnlySet<string>)`。

- [ ] **Step 1: 编写格式往返和损坏回退测试**

```csharp
[WpfFact]
public void Corrupt_rich_content_falls_back_to_plain_text()
{
    var service = new RichTextDocumentService();
    var document = service.Load("not-json", "可恢复正文");
    Assert.Contains("可恢复正文", new TextRange(document.ContentStart, document.ContentEnd).Text);
}
```

- [ ] **Step 2: 编写图片导入和孤立清理测试**

```csharp
[Fact]
public async Task Imported_media_uses_note_scoped_random_name()
{
    var service = new NoteMediaService(tempRoot);
    var relative = await service.ImportAsync(noteId, sourcePng);
    Assert.StartsWith($"{noteId.Value:N}/", relative.Replace('\\', '/'));
    Assert.True(File.Exists(Path.Combine(tempRoot, relative)));
}
```

- [ ] **Step 3: 运行服务测试并确认失败**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --filter "RichTextDocumentServiceTests|NoteMediaServiceTests"`

- [ ] **Step 4: 实现限定节点的 JSON 序列化器**

只支持 `Paragraph`、`Run`、`Bold`、`Italic`、`Underline`、`List`、`ListItem` 和图片引用；未知节点转换为纯文本，不通过 `XamlReader` 反序列化外部类型。

- [ ] **Step 5: 实现媒体导入、路径校验和孤立清理**

所有目标路径经 `Path.GetFullPath` 验证仍位于媒体根目录内；仅接受 `.png`、`.jpg`、`.jpeg`、`.gif`、`.bmp`、`.webp`。

- [ ] **Step 6: 运行服务测试和全部测试**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`

### Task 4: 实现笔记页面 ViewModel 与自动保存协调器

**Files:**
- Create: `ViewModels/NoteCardViewModel.cs`
- Create: `ViewModels/NotesViewModel.cs`
- Create: `ViewModels/NoteSaveCoordinator.cs`
- Test: `tests/ConvenientNote.Tests/ViewModels/NotesViewModelTests.cs`
- Test: `tests/ConvenientNote.Tests/ViewModels/NoteSaveCoordinatorTests.cs`

**Interfaces:**
- Consumes: Task 2 应用服务和 Task 3 文档服务。
- Produces: `NotesViewModel.IsEditorOpen`, `SelectedNote`, `FilteredNotes`, `OpenNoteCommand`, `CloseEditorCommand`, `NewNoteCommand`, `SaveNowCommand`；`NoteSaveCoordinator.Schedule`、`FlushAsync`、`RetryAsync`。

- [ ] **Step 1: 编写墙/编辑器切换和筛选失败测试**

```csharp
[Fact]
public async Task Double_click_command_opens_selected_note_in_editor()
{
    await viewModel.InitializeAsync();
    var note = Assert.Single(viewModel.FilteredNotes);
    viewModel.OpenNoteCommand.Execute(note);
    Assert.True(viewModel.IsEditorOpen);
    Assert.Same(note, viewModel.SelectedNote);
}
```

- [ ] **Step 2: 编写自动保存版本顺序失败测试**

```csharp
[Fact]
public async Task Older_save_never_overwrites_newer_edit()
{
    coordinator.Schedule("第一版");
    coordinator.Schedule("第二版");
    await coordinator.FlushAsync();
    Assert.Equal(["第二版"], savedPayloads);
}
```

- [ ] **Step 3: 运行 ViewModel 测试并确认失败**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --filter "NotesViewModelTests|NoteSaveCoordinatorTests"`

- [ ] **Step 4: 实现命令、筛选和保存状态机**

保存状态使用 `Idle`、`Saving`、`Saved`、`Failed`；每篇笔记只允许一个保存任务运行，变化版本通过递增 `long` 比较。

- [ ] **Step 5: 运行 ViewModel 测试和全部测试**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`

### Task 5: 构建笔记墙界面

**Files:**
- Create: `Views/NotesView.xaml`
- Create: `Views/NotesView.xaml.cs`
- Create: `Views/NoteWallControl.xaml`
- Create: `Views/NoteWallControl.xaml.cs`
- Modify: `MainWindowViewModel.cs`
- Modify: `NavigationItemViewModel.cs`
- Modify: `App.xaml.cs`

**Interfaces:**
- Consumes: Task 4 `NotesViewModel`。
- Produces: “笔记 / 记录想法与资料”导航项、搜索/筛选工具栏、可拖动卡片和双击打开事件。

- [ ] **Step 1: 将导航注册从 `TestingTodoView` 切换为 `NotesView`**

```csharp
NavigationItems.Add(new NavigationItemViewModel(
    NavigationSection.Testing,
    nameof(NotesView),
    "笔记",
    "记录想法与资料",
    PackIconKind.NotebookOutline));
```

- [ ] **Step 2: 建立笔记墙布局和状态绑定**

墙模式不显示完成复选框、待办优先级、月份和天气；卡片显示标题、摘要、标签、修改时间、置顶和收藏。

- [ ] **Step 3: 实现拖动、双击、整理和键盘焦点规则**

拖动超过系统阈值后不触发打开；双击未发生拖动时执行 `OpenNoteCommand`。

- [ ] **Step 4: 构建 WPF 项目并修复 XAML 错误**

Run: `dotnet build ConvenientNote.csproj`

### Task 6: 构建原生 WPF 富文本编辑器

**Files:**
- Create: `Views/RichNoteEditorControl.xaml`
- Create: `Views/RichNoteEditorControl.xaml.cs`
- Modify: `Views/NotesView.xaml`
- Modify: `ViewModels/NotesViewModel.cs`

**Interfaces:**
- Consumes: Task 3 文档/媒体服务和 Task 4 保存协调器。
- Produces: 格式工具栏、标题编辑、本地图片选择/粘贴/拖放、保存状态、返回行为和快捷键。

- [ ] **Step 1: 创建编辑器顶部栏和 `RichTextBox`**

工具栏绑定 WPF `EditingCommands.ToggleBold`、`ToggleItalic`、`ToggleUnderline`、`ToggleBullets`、`ToggleNumbering`、`AlignLeft`、`AlignCenter`、`AlignRight`。

- [ ] **Step 2: 实现标题级别、删除线和文字颜色命令**

所有格式操作作用于当前选择区；无选择时修改插入点格式，保持 WPF 标准撤销栈。

- [ ] **Step 3: 实现文件选择、剪贴板和拖放图片插入**

导入成功后用相对媒体 URI创建 `Image`；失败时保持当前文档并显示错误提示。

- [ ] **Step 4: 将正文变化接入延迟自动保存和显式保存**

`TextChanged` 只调度保存；`Ctrl+S`、返回、切换笔记和窗口关闭调用 `FlushAsync`。

- [ ] **Step 5: 添加窄窗口触发器与无障碍名称**

窗口宽度不足时折叠次要格式按钮和辅助栏；核心按钮设置 `AutomationProperties.Name` 和工具提示。

- [ ] **Step 6: 构建并运行全部测试**

Run: `dotnet build ConvenientNote.csproj`

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`

### Task 7: 完成回收站、兼容迁移和端到端验收

**Files:**
- Modify: `ViewModels/TrashViewModel.cs`
- Modify: `Views/TrashView.xaml`
- Modify: `MainWindow.xaml.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: Task 2 软删除/恢复能力、Task 4 保存刷新能力。
- Produces: 笔记删除恢复、关闭前保存和用户可见功能说明。

- [ ] **Step 1: 将笔记删除改为进入回收站并提供恢复/彻底删除**

待办既有行为保持不变；笔记卡菜单显示“移到回收站”，回收站按类型显示来源。

- [ ] **Step 2: 在窗口关闭前刷新当前编辑器**

关闭事件等待 `NotesViewModel.FlushAsync`；保存失败时取消关闭并允许用户重试或明确放弃。

- [ ] **Step 3: 更新 README 的笔记功能和数据目录说明**

写明数据库、媒体目录、快捷键和旧“待测试”数据自动保留行为。

- [ ] **Step 4: 运行完整验证**

Run: `dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj`

Run: `dotnet build ConvenientNote.slnx -c Release`

- [ ] **Step 5: 手动验收关键路径**

启动应用后依次验证：旧数据可见、新建、双击打开、格式化、粘贴与拖放图片、自动保存、搜索、笔记本、标签、置顶、收藏、关闭重开、删除恢复、窄窗口布局。

- [ ] **Step 6: 检查差异和工作区状态，不提交**

Run: `git diff --check`

Run: `git status --short`
