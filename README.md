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

当前 Prompt 数据是代码内置的三条测试数据。Prompt JSON 存储和 CRUD 管理属于下一阶段，管理器窗口目前用于查看 MVP 数据和验证托盘入口。

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
Infrastructure/
  NativeMethods.cs
  TrayIconService.cs
Views/
  SuggestionWindow.xaml(.cs)
  PromptManagerWindow.xaml(.cs)
ViewModels/
  PromptManagerViewModel.cs
```

## 已知边界

- 某些自绘控件或高权限窗口无法提供 UI Automation caret，也可能拒绝低权限进程的输入注入；此时会退回窗口位置。
- MVP 尚未监听鼠标低级 Hook，也未处理 IME 组合过程，因此光标在同一窗口内鼠标跳转和复杂中文输入法场景会在后续阶段增强。
- 暂未接入 JSON/SQLite、拼音和模糊匹配。
