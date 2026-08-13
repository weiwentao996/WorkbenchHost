# WorkbenchHost

Language: English | [Simplified Chinese](README.zh-CN.md)

WorkbenchHost is a standalone editor-style Windows host. It works with real files and folders instead of generated source stubs. All application adapters share one real `db.go` activation file.

## Quick Start

1. Double-click `Start Workbench.cmd`.
2. Open `db.go` and type the activation phrase configured by the target profile.
3. Switch tabs, press `F10`, or move focus away to hide the application and restore the code view.

If `profiles` is empty, the host starts in editor-only mode. You can still open folders and edit files; use `File > Import Application...` to add the first adapter.

`Start Workbench.cmd` is the only launcher needed. At startup, the host automatically loads every valid `.json` profile under `profiles`. Add as many applications as needed and give each one a unique activation phrase; typing that phrase in the shared `db.go` selects and opens the corresponding application. Application-specific launch scripts are not required.

## Editor Features

- `File > Open Folder` opens and remembers a real workspace folder.
- Open, create, save, save as, and close real files.
- Undo, redo, cut, copy, paste, find, and go to line.
- `Ctrl+N` new file, `Ctrl+O` open file, `Ctrl+S` save, `Ctrl+W` close tab.
- `Ctrl+F` find, `Ctrl+G` go to line, `Ctrl+J` toggle the output panel.
- Application opacity can be adjusted from `0%` to `100%`.
- `B/W` toggles grayscale display.

Workspace state is stored in `%LOCALAPPDATA%\WorkbenchHost\workspace-state.json`. It includes the last folder, opened files, active tab, and window layout. Missing or moved files are skipped during restore.

## Application Profiles

Each `.json` file in `profiles` describes one embeddable Windows application.

### Quick Import

Use `File > Import Application...` (`Ctrl+Shift+I`) to generate a profile:

1. Select the target EXE.
2. Enter a display name and activation phrase.
3. Adjust arguments, process name, window class, opacity, and close behavior when needed. Process name and window class can be left empty for automatic detection.
4. Select `Import`.

The profile is saved under `profiles`, loaded immediately, and opened as a real JSON file. Multi-window applications should provide the actual main-window `windowClass`.

### JSON Template

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
  "triggerFile": "db.go",
  "defaultOpacity": 100,
  "focusProtection": true,
  "enableGrayscale": false
}
```

### Field Reference

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Profile schema version; must currently be `1`. |
| `id` | Unique adapter identifier used internally and in logs. |
| `displayName` | Name shown in the host UI and help text. |
| `windowTitle` | Host title when this profile is loaded alone; defaults to `displayName - Code`. |
| `executable` | Target EXE path. Supports absolute paths, paths relative to the host directory, and `%VAR%` environment variables. The file must exist. |
| `workingDirectory` | Target working directory, using the same path rules; defaults to the EXE directory. |
| `arguments` | Startup argument string; may be empty. |
| `processName` | Optional process name without `.exe`; used to find an existing instance. When empty, it is inferred from the EXE file name. |
| `windowClass` | Optional exact top-level window class. Set this for multi-window applications when needed. When empty, visible windows are scored by size, title, and window type. |
| `attachExisting` | When `true`, prefer embedding an already running instance; when `false`, start a new process first. |
| `launchTimeoutSeconds` | Maximum seconds to wait for a target window; defaults to `45`. |
| `closeWithHost` | When `true`, closing the host closes the target; when `false`, the target is hidden and detached. |
| `killAfterMilliseconds` | Wait after sending `WM_CLOSE` before force termination; defaults to `1200`. |
| `activationPhrase` | Phrase typed into `db.go` to launch this adapter. It is removed as soon as it is recognized. |
| `workspaceDirectory` | Initial Explorer directory. Relative paths must stay under the host directory; absolute paths are supported. |
| `triggerFile` | File monitored for activation. Profiles sharing one gateway should all use `db.go`. |
| `defaultOpacity` | Initial target-window opacity from `0` to `100`; defaults to `100`. |
| `focusProtection` | When `true`, return to code if the target loses foreground focus. |
| `enableGrayscale` | When `true`, enable the toolbar `B/W` control. |

Relative paths are resolved from the directory containing `WorkbenchHost.exe`. On a no-argument launch, invalid profiles or profiles whose EXE is missing are skipped. If none remain, the host stays available as an editor-only workspace. Explicitly launching an invalid profile reports the error.

At runtime, the host first tries native Win32 embedding and verifies the resulting parent relationship. If the target rejects reparenting or changes its parent later, the host automatically switches to a borderless overlay that follows the editor surface. Protected, elevated, exclusive-fullscreen, and capture-blocked windows may still require external mode.

When a hosted application recreates its window or restores fullscreen styles after `Win+Tab`, the host hides the detached window, removes its separate task-switcher entry, and attempts to embed it again before falling back to overlay mode.

The legacy `virtualFileName`, `virtualSource`, and `files` fields are retained only for backward compatibility. The real editor does not use them; omit them from new profiles.

## Build and Compatibility

Run `build.cmd` to compile `WorkbenchHost.exe` with the .NET Framework C# compiler included with Windows. The host does not require `pwsh.exe` and supports Windows PowerShell 5.1 environments on older Windows 10 systems.

The configured target applications must be installed on the computer. Closing the host closes the currently embedded application when its profile has `closeWithHost` enabled.
