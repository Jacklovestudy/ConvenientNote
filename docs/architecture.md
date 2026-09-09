# 模块化架构约定

本软件采用 WPF 模块化单体：一个桌面进程，五个独立业务模块。模块拥有自己的 UI、应用操作、领域模型、数据存储和公开契约。独立项目用于约束依赖，不要求每个模块具有相同数量的实体或服务。

## 项目与依赖

```text
UI/
  ConvenientNote.Desktop/             启动、窗口、导航、模块装配、集成适配器
  ConvenientNote.Notes.UI/            笔记与回收站界面
  ConvenientNote.Todos.UI/            待办界面
  ConvenientNote.Calendar.UI/         日历界面
  ConvenientNote.ColorPicker.UI/      取色器界面
  ConvenientNote.DesktopPet.UI/       鹈鹕桌宠与设置页
  ConvenientNote.UI.Common/           窗口样式、界面生命周期与传输协调
src/
  App/                               五个模块的 Application 项目
  Modules/                           五个模块的 Domain 项目
  Infrastructure/                    五个模块的存储与平台实现
  Contracts/                         五个模块的公开接口
  Shared/
    ConvenientNote.Platform.Contracts/       工作区身份接口
    ConvenientNote.Platform.Infrastructure/  工作区元数据和启动迁移标记
  Compatibility/                     旧 SQLite/JSON 格式读取和一次性迁移
```

- `UI → Application → Domain`。
- `Infrastructure → Application / Domain`，实现应用层定义的端口。
- `Contracts` 不引用领域实体，只公开必要的接口和 DTO。
- 业务模块不得引用 Host、Compatibility 或其他模块内部项目。
- Host 作为装配入口允许引用具体实现；跨模块适配器只通过双方约定的端口交换数据。
- Domain 不依赖 WPF、Prism、EF 或 SQLite。
- 不建立共享业务实体或万能仓储；共享工作区仅传递 Guid 和名称。

架构测试直接检查 csproj 引用图、缺失引用和依赖环，新增项目时一起运行。

## 模块归属

| 模块 | 领域和应用职责 | 基础设施 | UI |
| --- | --- | --- | --- |
| Notes | 笔记、笔记本标识、标签、收藏、清单、回收站、导入导出流程 | `notes_records`、`Notes/Media`、`.cnote` 归档 | 便签墙、编辑器、回收站 |
| Todos | 独立 TodoItem、优先级、完成和计划日期 | `todos_items`、天气服务实现 | 今日待办、待办箱、已完成 |
| Calendar | 独立 CalendarEvent、时间范围、全天/定时日程、改期 | `Calendar_Events` | 完整日历与精简日历 |
| ColorPicker | ColorValue、最近颜色及历史操作 | `ColorPicker/history.json`、Windows 屏幕采集 | 取色页面、取色浮层 |
| DesktopPet | 动作状态、设置与位置 | `DesktopPet/settings.json` | 鹈鹕窗口、矢量动画与设置页 |

Calendar 可以不接入待办模块。接入时 Host 的 `TodoScheduleAdapter` 将 `ITodoCalendarApi` 转换为日历的 `ITodoScheduleSource`。待办投影不持久化为日程；待办改期/完成调用 Todos，独立日程改期/完成调用 Calendar。两类数据可以拥有相同 Guid，仍以来源区分。

日程时间当前为本地墙上时间，全天区间采用起点包含、终点不包含。第一版 UI 支持创建全天或当天定时日程、改期、完成和删除；未增加重复日程、时区转换或通知调度。

知识点清单仍作为 Notes 内的特殊笔记保存，保留 `__app_knowledge_memo` 标识。旧笔记中的待办字段通过只读 LegacyNoteMetadata 保留，仅用于兼容导入导出，不增加笔记领域的任务行为。

## 主窗口与模块生命周期

`DesktopModules` 集中装配服务和导航项目。主窗口依赖 `NavigationCatalog` 和 `IWorkspaceContext`，默认进入笔记。

离开/关闭前保存通过 `IPageLifecycle` 完成，失败允许页面阻止操作。主窗口不识别 NotesView。精简日历通过 `ICompactModeProvider` 提供内容，主窗口负责窗口尺寸和状态，日历模块负责视图。

WPF 页面保留原有 CLR 命名空间以降低迁移风险，但位于各自 UI 程序集。跨程序集资源必须使用 assembly/pack URI；公共主题在 UI.Common，笔记内置清单嵌入 Notes.UI。富文本文档服务依赖 WPF，留在 Notes.UI，并由 Host 注入与媒体存储相同的根目录。

## 数据迁移与恢复

默认根目录仍是 `%LocalAppData%/ConvenientNote`，开发验证通过 `CONVENIENTNOTE_DATA_DIRECTORY` 隔离。

```text
ConvenientNote.db              原始数据库，迁移不修改
workspaces.json                原始 JSON（如存在）
Media/                        原始媒体，迁移不修改
MigrationBackup-v1/            一致性 SQLite 快照、JSON 副本、快照标记
ConvenientNote.Modules.db      新模块数据库及工作区元数据
Notes/Media/                  新笔记模块独立媒体副本
ColorPicker/history.json       新取色历史
```

首次运行前关闭旧版本。迁移通过 SQLite 在线备份读取包含 WAL 已提交数据的一致性快照，在快照上执行旧格式升级，原数据库保持不变。

`testing` 分类迁入 Notes，`day-todo` 迁入 Todos。ID、正文、时间、删除状态、布局及知识点标识保留；未知分类中止迁移，不默默丢弃。媒体复制保留相对路径，临时文件完成后原子重命名，不覆盖已复制文件，拒绝源和目标的重解析点。

在全部记录和媒体导入成功前，不写 `modular-ddd-v1` 完成标记，也不打开业务页面。各模块导入只插入缺失 ID；失败后重试不会覆盖已导入记录。标记写入后不再自动读取旧库，因此不会复活新版已删除的内容。

Compatibility 仅为旧格式迁移和原格式回归测试保留。当前模块没有对该目录的项目引用；旧 WorkspaceApplicationService 不是当前业务入口。

如果未知分类导致失败，快照会固定保留。应先备份整个数据目录并检查原始记录；确认修复方案后，在未完成迁移的情况下将 `MigrationBackup-v1` 改名留档，下一次启动才会重新从原始数据取快照。不要删除完成标记来强制重导已有新版数据。

回到旧版只能看到迁移时保留的旧数据；新版产生的编辑不会同步回旧数据库。跨版本切换前导出需要保留的笔记。

## 验证与维护

```powershell
dotnet build ConvenientNote.slnx
dotnet test ConvenientNote.slnx --artifacts-path .codex-artifacts/verification
dotnet run --project UI/ConvenientNote.Desktop/ConvenientNote.Desktop.csproj
```

`tests/ConvenientNote.Desktop.Smoke` 使用指定的隔离目录启动真实 WPF/Prism 应用，在屏幕外检查默认页、全部导航、模块图片路径、日程保存和精简模式，并渲染页面 PNG；不会调用实际屏幕取色。

```powershell
dotnet run --project tests/ConvenientNote.Desktop.Smoke --artifacts-path .codex-artifacts/smoke-build -- .codex-artifacts/smoke-data
```

原回归测试中使用旧故障注入仓储的案例，通过 `tests/ConvenientNote.Tests/Compatibility` 适配到新应用服务。适配器仅用于测试，不进入产品程序集。独立模块测试直接验证新领域和实际存储。
