# WorkbenchHost

语言：[简体中文](README.zh-CN.md) | [English](README.md)

WorkbenchHost 是一个独立运行的编辑器式 Windows 宿主。它使用真实文件和真实文件夹，不生成伪造代码文件；所有应用适配器共用同一个真实的 `config.yaml` 作为启动入口。

## 快速开始

1. 双击 `Start Workbench.cmd`。
2. 打开 `config.yaml`，输入目标 profile 配置的激活关键词。
3. 切换标签、按 `F10` 或将焦点移到其他窗口，即可隐藏目标程序并恢复代码界面。

如果 `profiles` 为空，宿主会以纯编辑器模式启动。此时仍可以打开文件夹和编辑文件，使用 `File > Import Application...` 导入第一个适配器即可。

只需要 `Start Workbench.cmd` 这一个启动入口。宿主启动时会自动加载 `profiles` 下所有有效的 `.json` 配置；可以导入任意数量的程序，并为每个程序设置不同的激活关键词。在共用的 `config.yaml` 中输入对应关键词，即可选择并打开目标程序，不需要为特定程序单独编写启动脚本。

## 编辑器功能

- `File > Open Folder`：打开并记住真实工作区文件夹。
- 打开、新建、保存、另存为和关闭真实文件。
- 撤销、重做、剪切、复制、粘贴、查找和跳转行。
- `Ctrl+N` 新建文件，`Ctrl+O` 打开文件，`Ctrl+S` 保存，`Ctrl+W` 关闭标签。
- `Ctrl+F` 查找，`Ctrl+G` 跳转行，`Ctrl+J` 显示或隐藏输出面板。
- 目标窗口透明度支持 `0%` 到 `100%`。
- `B/W` 切换黑白显示。

工作区状态保存在 `%LOCALAPPDATA%\WorkbenchHost\workspace-state.json`，包括最后打开的文件夹、已打开文件、当前标签和窗口布局。已经删除或移动的文件会在恢复时自动跳过。

## profiles 配置

`profiles` 目录中的每个 `.json` 文件代表一个可嵌入的 Windows 程序。

### 快捷导入

使用 `File > Import Application...`（`Ctrl+Shift+I`）可以快速生成 profile：

1. 选择目标 EXE。
2. 填写显示名称和激活关键词。
3. 按需调整启动参数、进程名、窗口类、透明度和关闭策略。进程名和窗口类可以留空，由宿主自动识别。
4. 点击 `Import`。

生成的 profile 会保存到 `profiles`、立即加载，并作为真实 JSON 文件在编辑器中打开。多窗口程序应填写实际主窗口的 `windowClass`。

### JSON 模板

```json
{
  "schemaVersion": 1,
  "id": "my-app",
  "displayName": "My App",
  "windowTitle": "workspace - Code",
  "executable": "%ProgramFiles%\\MyApp\\MyApp.exe",
  "workingDirectory": "%ProgramFiles%\\MyApp",
  "arguments": "",
  "processName": "MyApp",
  "windowClass": "",
  "attachExisting": false,
  "launchTimeoutSeconds": 30,
  "closeWithHost": true,
  "killAfterMilliseconds": 1500,
  "activationPhrase": "hello my app",
  "workspaceDirectory": ".",
  "triggerFile": "config.yaml",
  "defaultOpacity": 100,
  "focusProtection": true,
  "enableGrayscale": false
}
```

### 字段说明

| 字段 | 含义 |
| --- | --- |
| `schemaVersion` | 配置格式版本，目前必须为 `1`。 |
| `id` | 适配器唯一标识，只用于内部识别和日志。 |
| `displayName` | 界面和帮助信息中显示的名称。 |
| `windowTitle` | 只加载此 profile 时的宿主窗口标题；默认是 `displayName - Code`。 |
| `executable` | 目标 EXE 路径。支持绝对路径、相对宿主目录路径和 `%VAR%` 环境变量。文件必须存在。 |
| `workingDirectory` | 目标程序工作目录，路径规则同 `executable`；省略时取 EXE 所在目录。 |
| `arguments` | 启动参数字符串，可以为空。 |
| `processName` | 可选的进程名，不带 `.exe`；用于查找已运行实例。留空时从 EXE 文件名自动推断。 |
| `windowClass` | 可选的顶级窗口类名，必须精确匹配。多窗口程序必要时填写真正的主窗口类名；留空时根据面积、标题和窗口类型自动评分。 |
| `attachExisting` | `true` 时优先嵌入已经运行的目标程序；`false` 时先启动新进程。 |
| `launchTimeoutSeconds` | 等待目标窗口出现的最长秒数，默认 `45`。 |
| `closeWithHost` | `true` 时关闭宿主也关闭目标程序；`false` 时只隐藏并释放窗口。 |
| `killAfterMilliseconds` | 发送 `WM_CLOSE` 后等待的毫秒数，超时才强制终止；默认 `1200`。 |
| `activationPhrase` | 在 `config.yaml` 中输入的启动关键词。识别后会立即从编辑器文本中移除。 |
| `workspaceDirectory` | Explorer 初始目录。相对路径必须位于宿主目录内，也可以填写绝对路径。 |
| `triggerFile` | 关键词监听文件。需要共用同一入口的 profile 都应填写 `config.yaml`；旧的 `db.go` 配置会自动迁移。 |
| `defaultOpacity` | 目标窗口初始透明度，范围 `0` 到 `100`，默认 `100`。 |
| `focusProtection` | `true` 时目标窗口失去前台焦点后自动切回代码。 |
| `enableGrayscale` | `true` 时启用工具栏的 `B/W` 控制。 |

路径中的相对路径以 `WorkbenchHost.exe` 所在目录为基准。默认启动会扫描 `profiles`，无法找到 EXE 或配置无效的 profile 会被跳过；如果全部跳过，宿主仍会以纯编辑器模式运行。通过命令行显式指定的无效 profile 会直接报错。

运行时宿主会先尝试原生 Win32 嵌入，并验证窗口父子关系。如果目标程序拒绝重新挂载，或运行中自行脱离宿主，宿主会自动切换为跟随编辑器区域的无边框贴合模式。受保护窗口、权限更高的程序、独占全屏程序和禁止捕获画面的程序仍可能只能外部运行。

如果被托管程序在 `Win+Tab` 后重建窗口或恢复全屏样式，宿主会先隐藏脱离的窗口、移除其独立任务切换项并尝试重新嵌入，连续失败后才降级到贴合模式。

旧版本中的 `virtualFileName`、`virtualSource` 和 `files` 字段仅为兼容旧配置保留，真实编辑器不会使用它们；新 profile 不要配置这些字段。

## 构建和兼容性

运行 `build.cmd` 可使用 Windows 自带的 .NET Framework C# 编译器构建 `WorkbenchHost.exe`。宿主不要求 `pwsh.exe`，可用于只有 Windows PowerShell 5.1 的旧版 Windows 10 环境。

配置的目标程序仍必须安装在当前电脑上。对于启用了 `closeWithHost` 的 profile，关闭宿主会同时关闭当前嵌入的目标程序。
