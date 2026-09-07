# 日历与桌面模式实施计划

> 2026-09-07 用户变更：取消 Win+D 保留及 Explorer 挂载要求。当前实现已替换为主窗口内的精简日历布局，返回时恢复窗口大小与状态。原生桌面宿主、托盘、监测器及对应测试已移除。以下记录保留为历史实施记录。

用户已批准完整月历及精简桌面模式，明确要求 Win+D 后仍可见可操作。保留上一轮物理目录变更，不提交、不推送。继续在当前工作区执行，避免丢失尚未提交的目录整理。

## 设计与接口

- Calendar 功能物理目录容纳日历页面、日期计算、日程操作、桌面窗口及挂载服务。保持现有 Indigo 风格。
- Note 与 NoteSnapshot 新增可空 DateTime PlannedDate 和 DateTimeOffset CompletedAt；旧记录无日期，不能猜测历史日期。SQLite 增列兼容旧库，JSON 同步支持。
- WorkspaceApplicationService 提供 CreateScheduledTodoAsync(WorkspaceId, string, DateTime, CancellationToken=default)、SetNotePlannedDateAsync(WorkspaceId, NoteId, DateTime?, CancellationToken=default)，新增 EventHandler WorkspaceChanged 在成功写入后通知；写入序列化避免多窗口覆盖。
- Calendar 页面使用同一 ScheduleViewModel 实例同时服务完整页和精简页；日期网格按周一开始固定六周，农历与主要节日离线计算，休假调休仅使用核实过的年度数据，未知年份明确提示无安排数据。
- ScheduleViewModel 提供 DesktopModeRequested 事件；桌面宿主接入时创建精简 CalendarPanel，共享 ViewModel。返回完整界面保留所选日。
- 桌面模式实际挂载 Explorer 桌面层，不能用 Topmost 冒充；失败时保留主界面并显示原因。监视 Explorer 恢复，保存几何设置；托盘负责恢复与退出。

## 工作划分

1. 数据基础（主代理）：计划日期/完成时间、SQLite/JSON、应用服务、待办日期筛选与变更通知；先写持久化和日期行为测试，再实现。
2. 日历界面（子代理）：CalendarPanel、ScheduleViewModel、农历日历模型、日期选择/新增/完成/改期、桌面入口事件；添加日历边界测试。
3. 桌面宿主（子代理）：Features/Calendar/Desktop 下窗口、挂载器、配置和托盘管理；提供 Initialize(Window, ScheduleViewModel)、ShowDesktopAsync()、RestoreMainWindow()、Dispose()，主代理完成注册和启动绑定。
4. 集成验收（主代理＋审查代理）：独立输出完整构建测试；实际窗口截图和 Win+D 原生验证；桌面操作失败不得宣称成功。记录兼容性限制。

## 验证

测试运行使用 bin/CalendarValidation 输出，防止覆盖正在运行的软件。WPF 测试串行运行。临时验收实例使用独立数据目录，避免现有实例使用旧模型覆盖新数据。最终核对 XAML、数据回读、模式切换及窗口层级。

## 进度

- [x] 数据基础
- [x] 完整日历
- [x] 桌面宿主
- [x] 集成与验收

## 实机验收记录

- 使用独立数据库启动验收实例，新增日程后重启仍可读取；旧的无日期待办保持待安排状态。
- 桌面组件为 Explorer 桌面视图下的原生子窗口。Win+D 后窗口仍可见，命中测试仍落在组件上；不是置顶窗口。
- 桌面输入实际逐键输入标题（含空格）并按 Enter，任务成功写入。切换日期后返回完整页保留所选日期。
- 最小 360 × 480 DIP 尺寸在当前 125% 缩放下检查通过，农历和首项勾选框未裁切。截图保存在忽略目录 `bin/CalendarValidation/desktop-final.png`。
- 返回完整页后发送显示器变更消息，隐藏的桌面组件保持隐藏。
- 修复原生子窗口键盘输入根设置、隐藏窗口被定位操作重新显示、测试数据库误导入全局 JSON，以及辅助功能勾选仅改变外观的问题。
- 多屏混合 DPI 和 Explorer 重启恢复已实现防护及针对性测试，未在对应实机情形执行验收。未重启或终止用户的 Explorer。

## 最终验证

- `dotnet test ConvenientNote.slnx --nologo --verbosity minimal -p:BaseOutputPath=bin/CalendarTest/ --logger 'trx;LogFileName=calendar-final.trx' -- xUnit.ParallelizeTestCollections=false`：226 项通过，0 失败，0 跳过。
- 勾选交互回归测试先复现失败，再验证完成／取消完成及时间戳、初始化保护、失败回退、忙碌回退共 4 项通过。测试使用 WPF Dispatcher 同步上下文并等待保存及替换行加载完成。
- `dotnet build ConvenientNote.csproj -c Release --nologo --verbosity minimal`：成功，0 警告，0 错误。
- 新版入口：`bin/Release/net10.0-windows/ConvenientNote.exe`。保留用户原有 Debug 实例，交付时提示先正常退出旧版再启动新版。
- 所有修改保留在工作区，未提交、未推送。
