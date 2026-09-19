# TypeSense

TypeSense 是一个 Windows 全局 Prompt 实时补全 MVP，目标是在普通文本输入框中输入缩写时，自动弹出 Prompt 候选，并使用键盘完成替换。

## 当前 MVP

- .NET 8 + WPF + Win32
- `WH_KEYBOARD_LL` 全局键盘监听
- 最近输入缓冲区与窗口切换重置
- 缩写前缀匹配、名称匹配和基础排序
- 无边框、置顶、不抢焦点候选窗口
- UI Automation → `GetGUIThreadInfo` → 窗口位置的光标定位 fallback
- `↑` / `↓`、数字 `1`～`9`、`Tab`、`Enter`、`Esc`
- 剪贴板保护、Backspace 删除和 Unicode Ctrl+V 注入
- 系统托盘、暂停/启用监听、开机启动菜单
- 指令管理界面：搜索、新增、编辑、删除和启用/禁用
- Prompt 与软件设置保存到当前用户的本地 JSON 配置
- 软件设置：全局监听、内容预览、数字键选择和 Enter 确认
- 根据指令名称自动生成全拼和首字母隐藏别名，管理界面不再维护缩写字段

首次启动时会将内置示例 Prompt 写入当前用户的本地配置目录；后续可以通过管理器维护自己的指令。

## 运行

需要 Windows 11、.NET 8 SDK 和桌面开发组件：

```powershell
dotnet build TypeSense.sln
dotnet run --project TypeSense.csproj
```

启动后程序默认驻留系统托盘。在 Notepad 中输入 `zw`，候选窗口应出现；继续输入 `zwrs` 后按 `Tab`，触发字符串会被替换为完整 Prompt。

## 目录

```text
App.xaml(.cs)
Models/
Services/
  AppController.cs
  KeyboardHookService.cs
  InputBufferService.cs
  PromptMatchService.cs
  CaretPositionService.cs
  TextInsertionService.cs
  PromptCatalogService.cs
  PromptStorageService.cs
  AppSettingsService.cs
  PinyinAliasService.cs
Infrastructure/
  NativeMethods.cs
  TrayIconService.cs
Views/
  SuggestionWindow.xaml(.cs)
  PromptManagerWindow.xaml(.cs)
  PromptEditorWindow.xaml(.cs)
ViewModels/
  PromptManagerViewModel.cs
  SettingsViewModel.cs
```

## 已知边界

- 某些自绘控件或高权限窗口无法提供 UI Automation caret，也可能拒绝低权限进程的输入注入；此时会退回窗口位置。
- MVP 尚未监听鼠标低级 Hook，也未处理 IME 组合过程，因此光标在同一窗口内鼠标跳转和复杂中文输入法场景会在后续阶段增强。
- 当前使用 JSON 持久化，已支持基础拼音别名匹配，尚未接入 SQLite 和更复杂的模糊匹配。
