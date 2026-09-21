# ZCue 项目协作说明

## 项目定位

ZCue 是 Windows 全局 Prompt 实时补全工具。程序常驻系统托盘，在任意文本输入场景中监听用户输入，根据 Prompt 名称生成的拼音/首字母隐藏别名和名称文本匹配候选，并在当前光标附近显示不抢焦点的候选窗；确认后删除触发片段并插入完整 Prompt。

当前实现是单项目 WPF 应用，目标框架为 `.NET 8` Windows 桌面，使用 Win32 API、Windows UI Automation 和 `PinYinConverterCore`。`ref/` 中的 JavaScript/HTML 是浏览器插件参考实现，不参与 Windows 项目编译；可参考其匹配、排序和交互思路，但不要把 DOM 代码直接迁移到 WPF。

## 技术入口与运行方式

- `App.xaml.cs` 是 WPF 应用入口。应用使用显式关闭模式，启动时创建并启动 `AppController`，退出时释放 Hook、窗口和托盘资源。
- `ZCue.csproj` 定义 `net8.0-windows`、WPF、Windows Forms、PerMonitorV2 DPI 和 `PinYinConverterCore 1.0.2` 依赖；输出类型是托盘型 `WinExe`。
- `app.manifest` 使用 `asInvoker` 且 `uiAccess=false`。程序不能自动越过权限边界操作高权限目标窗口。
- 常用命令：

  ```powershell
  dotnet build ZCue.sln
  dotnet build ZCue.csproj --configuration Debug
  dotnet run --project ZCue.csproj
  ```

- 当前没有独立测试项目。修改后至少执行一次构建，并进行手动冒烟验证：启动程序后在 Notepad 等普通文本框输入 `zw`，确认候选出现；继续输入 `zwrs`，用 `Tab` 或 `Enter` 确认，检查触发片段被删除且完整内容插入。还应验证 `↑/↓`、数字键、`Esc`、中文名称/拼音匹配及托盘暂停/恢复。

## 目录与核心职责

| 位置 | 职责 |
| --- | --- |
| `App.xaml(.cs)` | WPF 资源和应用生命周期入口。 |
| `Models/PromptItem.cs` | Prompt 数据、使用次数、启用状态以及 `PromptMatch`/匹配类型模型。 |
| `Models/PromptAlias.cs` | 持久化的全拼/首字母别名、分段信息和输入范围到名称高亮范围的映射。 |
| `Models/AppSettings.cs` | 候选预览、数字键选择、Enter 确认等设置。 |
| `Services/AppController.cs` | 应用编排中心，连接 Hook、缓冲区、匹配、候选窗、光标定位、文本插入、托盘和管理器。 |
| `Services/KeyboardHookService.cs` | 独立线程上的 `WH_KEYBOARD_LL` 键盘 Hook 和 `WH_MOUSE_LL` 鼠标按下监听；鼠标事件只观察、不拦截。 |
| `Services/InputBufferService.cs` | 最近输入缓冲区、当前 token、Backspace、光标前文本同步和边界重置。默认最大缓冲长度为 30。 |
| `Services/PromptMatchService.cs` | 读取已保存别名和名称，尝试当前输入的尾部候选片段，计算匹配质量、排序和名称高亮范围。 |
| `Services/PinyinAliasService.cs` | 根据 Prompt 名称生成全拼、首字母、多音字变体及分段映射；不负责 UI。 |
| `Services/PromptCatalogService.cs` | Prompt 的加载、规范化、增删改、启用状态、使用次数和别名刷新；是 Prompt 数据变更的统一入口。 |
| `Services/PromptStorageService.cs` | 将 Prompt 保存到用户本地 JSON。 |
| `Services/AppSettingsService.cs` | 设置加载、保存、线程安全快照和 `Changed` 通知。 |
| `Services/CaretPositionService.cs` | UI Automation 光标定位，并按 Win32 caret、窗口矩形、鼠标位置顺序 fallback。 |
| `Services/FocusedTextService.cs` | 通过 UI Automation 的 `TextPattern` 读取焦点控件中光标前的文本，用于 Ctrl+V、撤销和 IME 场景同步。 |
| `Services/TextInsertionService.cs` | 通过 `SendInput` 删除触发片段并优先注入 Unicode；失败时使用保护/恢复剪贴板的粘贴方案。 |
| `Services/StartupService.cs` | 读写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的开机启动项。 |
| `Infrastructure/NativeMethods.cs` | Win32 常量、结构体和 P/Invoke；包括 Hook、窗口、UI 定位和 `SendInput`。 |
| `Infrastructure/TrayIconService.cs` | 系统托盘图标、打开管理器、暂停/启用监听、开机启动和退出菜单。 |
| `Views/SuggestionWindow.xaml(.cs)` | 无边框、置顶、不激活的候选窗；负责主题、候选行、编号和匹配片段高亮。 |
| `Views/PromptManagerWindow.xaml(.cs)` | 指令管理和软件设置两个页面，处理搜索、双击编辑、删除、启用/禁用和设置交互。 |
| `Views/PromptEditorWindow.xaml(.cs)` | 新建/编辑 Prompt 名称、内容和启用状态的对话框；不提供手工缩写字段。 |
| `ViewModels/` | `PromptManagerViewModel` 管理列表筛选和 CRUD 刷新，`SettingsViewModel` 连接设置服务及运行时监听状态。 |
| `ref/` | 浏览器插件参考代码，仅用于理解行为和算法，不是构建输入。 |

## 关键调用链

```text
App
└─ AppController
   ├─ KeyboardHookService (WH_KEYBOARD_LL / WH_MOUSE_LL 专用线程)
   │  ├─ KeyboardInputEventArgs -> HandleKeyDown
   │  └─ 窗外鼠标按下 -> 清除候选；候选窗内点击交给 WPF 行选择
   ├─ InputBufferService -> 当前 token
   ├─ PromptCatalogService.GetEnabledItems()
   │  └─ PromptMatchService.Match()
   ├─ Dispatcher -> SuggestionWindow.ShowSuggestions()
   │  └─ CaretPositionService 定位候选窗
   └─ 确认候选 -> TextInsertionService.ReplaceAsync()
      ├─ SendInput Backspace
      └─ Unicode SendInput；失败时剪贴板 Paste 并恢复原内容
```

键盘 Hook 运行在专用线程，不能直接操作 WPF 控件；候选窗更新必须通过 `Dispatcher`。`AppController` 使用状态锁和版本号丢弃过期的 UI 更新。`SendInput` 产生的注入事件带有 injected 标记，Hook 必须忽略这些事件，避免文本插入递归触发匹配。

鼠标 Hook 也运行在该专用线程：点击候选窗口范围内交由 WPF 行处理，点击候选窗以外则清空候选状态并通过 Dispatcher 隐藏窗口。候选可见时，`AppController` 还会短间隔检查前台窗口，确保窗口切换后不会留下置顶候选框。

## 数据与持久化约定

- Prompt 文件为 `%LOCALAPPDATA%\ZCue\prompts.json`，设置文件为 `%LOCALAPPDATA%\ZCue\settings.json`；如果系统无法提供 LocalAppData，服务回退到程序目录。
- `PromptCatalogService` 是 Prompt 的唯一变更入口。新增或编辑时根据 `Name` 调用 `PinyinAliasService.RefreshAliases`，再由 `PromptStorageService` 持久化。
- `PromptItem.PinyinAliases` 是隐藏内部字段，不在管理界面展示。它保存全拼/首字母别名、匹配分段和高亮映射；匹配热路径只读取该字段，不应在每次按键时重新生成拼音。
- 加载没有别名的旧 JSON 时，目录服务会生成别名并回写；已有别名直接使用。修改名称时必须通过目录服务更新，确保别名与名称同步。
- `PromptStorageService` 和 `AppSettingsService` 使用大小写不敏感、缩进 JSON，并在本地文件不存在或读取失败时回退默认对象。修改错误处理时要特别注意不要造成用户 Prompt 或设置的意外覆盖。
- `UsageCount` 由目录服务统一累加并保存；候选排序依赖匹配质量、使用次数、匹配长度和名称长度等现有规则。

## 修改约束

1. 保持服务边界：全局 Hook、Win32 调用、UI Automation、文本注入、匹配算法和 WPF 渲染分别留在对应服务/视图中，不要把 `SendInput` 或存储逻辑散落到窗口代码。
2. 保持 `PromptItem`、`PromptMatch`、`PromptAlias` 的字段含义和高亮映射一致。改变别名结构时，同时检查 JSON 反序列化、克隆、目录规范化、匹配和候选高亮。
3. 不重新引入手工 `Abbreviation` 字段。用户可编辑的文本字段只有名称和内容，启用状态是独立开关；触发别名由名称自动派生并隐藏保存。
4. 修改输入状态时同时考虑鼠标点击外部、前台窗口切换、Backspace、空格/回车、方向键、Ctrl+A、Ctrl+V、撤销和 IME 提交；这些路径会重置或通过 `FocusedTextService` 同步缓冲区。
5. 候选窗必须继续使用无激活样式、置顶和 `SWP_NOACTIVATE`，不能因为刷新候选而抢走目标输入框焦点。
6. 修改文本插入时必须保留：目标窗口前台校验、注入事件过滤、Unicode 支持、触发串删除长度、剪贴板恢复和异常隔离。
7. `NativeMethods` 中的 P/Invoke 签名、结构体布局、Hook 消息循环和输入标志非常敏感，除非明确验证 Win32 行为，不要随意改名或调整字段类型。
8. 注意项目同时启用 WPF 和 Windows Forms。涉及 `Application`、`IDataObject`、`CheckBox` 等同名类型时使用完整命名空间或现有别名，避免引用歧义。
9. 运行时监听开关只影响当前进程；开机启动是当前用户 Registry 设置；Prompt 和交互设置则保存到 JSON，不要混淆三者生命周期。
10. 不要把 `ref/` 浏览器实现中的 DOM、剪贴板事件或浏览器专用 API 直接复制到 Windows 服务；只迁移已经被当前 Win32/WPF 结构验证过的行为。

## 编码风格

- 使用文件级命名空间 `namespace ZCue...;`、可空引用类型、隐式 using、`sealed` 类、记录类型和现代集合表达式。
- 长文件按功能使用成对的 `// SECTION 名称` 与 `// !SECTION 名称` 标记；新增、移动或删除代码时保持标记名称、配对和嵌套正确。
- 方法和属性使用 PascalCase，私有字段使用 `_camelCase`；事件使用 `Action`/事件名语义命名。ViewModel 使用 `INotifyPropertyChanged` 和集中式 `OnPropertyChanged`。
- 现有注释和用户界面文本以中文为主；新增注释应说明平台约束或调用意图，不要复制大段实现说明。

## 修改后的检查清单

- 运行 `dotnet build ZCue.sln`，确认无编译错误；如果程序正在运行并锁定输出文件，先退出托盘实例后再构建。
- 运行 `git diff --check`，检查空白和补丁格式；确认没有误改用户本地数据文件、`bin/` 或 `obj/`。
- 对匹配/别名改动，至少验证中文名称、全拼、首字母、多音字和高亮范围，并验证 JSON 往返后结果一致。
- 对 Hook/输入注入改动，至少在 Notepad 等普通文本框验证监听、候选键盘操作、窗外点击关闭、应用切换后关闭、Unicode 插入、剪贴板内容和注入事件不递归；点击候选行仍应能选择。
- 对 UI/定位改动，验证候选窗不抢焦点、光标附近定位、屏幕边缘 fallback、深浅色主题和管理器双击编辑。
- 对存储/设置改动，检查 `%LOCALAPPDATA%\ZCue\prompts.json` 与 `settings.json` 的字段和加载回写行为；不要用测试数据覆盖用户现有配置。
