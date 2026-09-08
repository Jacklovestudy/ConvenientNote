# ColorPicker 模块

独立拥有颜色模型与最近取色历史，不依赖待办、笔记、日历或旧工作区存储。

## 项目边界

- Domain：不透明 sRGB 颜色值和 HEX / RGB 表示。
- Application：取色历史用例、持久化端口、屏幕采集端口及冻结图像数据。
- Infrastructure：Windows GDI 屏幕采集、独立 JSON 历史存储。
- UI：WPF 取色页、视图模型及冻结屏幕浮层。
- Contracts：只读历史查询，返回 DTO，供其他模块显式接入。

## Host 装配

以单例注册 `IColorHistoryStore` → `JsonColorHistoryStore(historyFilePath)`、
`ColorPickerService`、`IScreenCapture` → `WindowsScreenCapture`。
需要跨模块查询时，将 `IColorHistoryQuery` 映射到同一 `ColorPickerService` 实例。
把 `ConvenientNote.ColorPicker.UI.ColorPickerView` 注册为导航页面。
页面构造函数接收 `ColorPickerService` 和 `IScreenCapture`，自行设置 ViewModel，
不需要 Prism 的 ViewModel 自动定位。

`historyFilePath` 应位于应用数据目录的 `ColorPicker/history.json`。
JSON 文件只由本模块写入，不参与旧工作区整体覆盖保存。
历史最多 32 种颜色，重复取色移到首位，文件原子替换，写入失败保留之前的历史。
损坏的 JSON 会先备份为 `.invalid-<id>` 再以空历史启动。
历史读取失败或损坏文件无法备份时，不影响软件启动；页面显示警告，历史进入只读模式，
仍可取色和复制。恢复文件访问并重启后再加载，不覆盖无法读取的原始历史。

## 取色行为

点击屏幕取色时先抓取虚拟桌面，再显示冻结图像。浮层显示当前像素的色值，
左键确认，Esc 或切换应用取消。采样始终读取原始快照，避免预览框污染颜色。
坐标使用物理像素，支持负坐标显示器；原生取屏和浮层创建均限定线程 DPI 上下文。
取色结束后可复制 HEX / RGB，或点击最近颜色重新选择。

GDI 返回桌面合成的 8 位颜色值，不提供 HDR / 广色域校色功能。
受系统保护的窗口或无法抓取的桌面内容可能呈黑色。

## 验证

`dotnet test tests/ConvenientNote.ColorPicker.Tests/ConvenientNote.ColorPicker.Tests.csproj`

测试覆盖颜色格式、负坐标采样、快照不可变性、历史上限/去重/持久化隔离及保存失败。
UI 可独立编译；页面曾以模拟数据离屏渲染检查 640 和 1000 像素宽布局。
实际多显示器、不同 DPI、Esc 取消、剪贴板占用需在桌面环境进行交互验收。
