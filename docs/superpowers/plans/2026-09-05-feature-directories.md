# 功能物理目录整理计划

目标：将 WPF 功能文件按业务归组，保留现有行为、命名空间、数据库和项目边界。不提交 Git。

目录约定：
- Shell：主窗口、导航、窗口生命周期。
- Features/Notes：笔记页面和视图模型；Editor 放编辑器及文档辅助类，Knowledge 放知识清单，Backup 放备份，Media 放图片服务。
- Features/Todos：今日待办、待办箱、已完成和共用待办画布；Legacy 放旧待测试页面。
- Features/Calendar：日程页面及视图模型。
- Features/Review：复盘页面及视图模型。
- Features/Trash：回收站页面及视图模型。
- Shared/Weather：天气服务；Shared/Workspace：工作区传输协调。
- src：保留 Domain、Application、Infrastructure 分层。
- Resources：保留资源地址和嵌入资源名称。

执行步骤：
- [x] 运行现有测试建立基线；使用独立 bin/DirectoryLayout 输出，避免运行中的软件锁定文件。
- [x] 显式映射并移动所有根目录 Views、ViewModels、Services 文件以及窗口、导航、待办模型；移动前检查源和目标均在项目内且无覆盖。
- [x] 修正主窗口图标地址与源码读取测试的路径；保持 x:Class 和 CLR 命名空间，兼容 Prism 自动定位。
- [x] README 说明新目录及后续文件归属。
- [x] 完整构建、测试及差异检查；确认每个移动文件内容一致或仅有预期路径修正。

验收命令：`dotnet test ConvenientNote.slnx --nologo --verbosity minimal -p:BaseOutputPath=bin/DirectoryLayout/`、`git diff --check`。本次仅移动文件，无需新增镜像实现的测试。

验证结果：迁移 68 个文件，其中 67 个与原 Git 内容哈希完全一致，MainWindow.xaml 仅修正图标资源 URI。更新 2 个测试文件中的源码路径。独立输出目录完整构建通过；最终使用 -- xUnit.ParallelizeTestCollections=false，194/194 测试通过。期间并行测试出现 WPF PackagePart 资源加载异常，首次串行测试有一次动画缓存恢复超时，未修改业务代码或放宽断言。git diff --check 通过。全部变更未暂存、未提交。
