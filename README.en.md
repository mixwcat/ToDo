# Yijian Todo · 一件 Todo

[简体中文](README.md) · English

A vertical desktop task manager with classical Chinese poetry, nested tasks, and an AI companion to help refine your plans. Available for Windows and Ubuntu, with Chinese and English interfaces.

Windows uses C# / .NET 8 / WPF; Ubuntu uses C# / .NET 8 / Avalonia 11. Both interfaces share task logic, AI integration, storage, translations, and the poetry corpus. There is no embedded browser or WebView, and no background polling service.

[Download](https://github.com/mixwcat/ToDo/releases/latest) · [Releases](https://github.com/mixwcat/ToDo/releases) · [Report an issue](https://github.com/mixwcat/ToDo/issues)

## Download and install

Current version: **v0.1.3**. All downloads below target x86-64. The full edition is recommended for most users.

v0.1.3 adds **Sticky Notes** to both Windows and Ubuntu: press `F2` (including global hotkey support when unfocused) or click the top `✍` button to open multiple draggable sticky note windows, type text and paste clipboard bitmaps, image files, HTML images, or local image paths with `Ctrl+V`; note text and images stay in memory only without automatic disk persistence, clicking `✕` closes and discards the note while cleaning up temporary preview files, `💾` or `Ctrl+S` exports the note and its images as Markdown (`.md`), the note shortcut can be customized in Settings, and double-clicking any pasted image opens it in the system's default image viewer. See the [changelog](CHANGELOG.md).

| Platform | Full: includes the .NET runtime | Lite: uses the system .NET runtime |
|---|---|---|
| Windows 11 x64 | [Installer · 50.4 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.1/YijianTodo-0.1.1-windows-x64-setup.exe) | [Installer · 3.7 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.1/YijianTodo-0.1.1-windows-x64-lite-setup.exe) |
| Ubuntu 22.04 / 24.04 / 26.04 amd64 | [deb · 31.3 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.3/YijianTodo-0.1.3-ubuntu-amd64.deb) | [deb · 9.5 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.3/YijianTodo-0.1.3-ubuntu-amd64-lite.deb) |

Install either the full or Lite edition. Installers are available from [GitHub Releases](https://github.com/mixwcat/ToDo/releases/latest), rather than the source checkout.

### Windows

Run the `.exe` installer. It installs for the current user, creates desktop and Start menu shortcuts, and registers an uninstaller. Administrator privileges are not required. The default location is `%LOCALAPPDATA%\Programs\YijianTodo`.

The Lite edition requires [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0/runtime). The full edition includes its own runtime. Windows installers are currently unsigned.

### Ubuntu

Open a terminal in your download directory and install the full edition:

```bash
sudo apt install ./YijianTodo-0.1.3-ubuntu-amd64.deb
```

Launch it from the applications menu or run `yijian-todo`. Program files are installed under `/usr/lib/yijian-todo`.

Install the Lite edition the same way; your package sources must provide `dotnet-runtime-8.0`. The package declares required system libraries. It also recommends `fonts-noto-cjk` for Chinese text and `libsecret-tools` for remembering API keys; apt may install these additional packages.

X11 is supported directly; Wayland desktops use XWayland. The GNOME tray requires AppIndicator / KStatusNotifier support. When no tray is available, closing to the tray falls back to minimizing the window.

To uninstall:

```bash
sudo apt remove yijian-todo
```

### Updates, uninstalling, and checksums

Quit the application from its tray menu before updating or uninstalling. Both platforms preserve user tasks, preferences, and remembered API keys when uninstalling.

Linux v0.1.2 includes [SHA256SUMS.txt](https://github.com/mixwcat/ToDo/releases/download/v0.1.2/SHA256SUMS.txt); Windows v0.1.1 checksums are in [its release](https://github.com/mixwcat/ToDo/releases/tag/v0.1.1). Compare it with `Get-FileHash FILENAME -Algorithm SHA256` on Windows or `sha256sum FILENAME` on Ubuntu. If you download all installers, you can also run `sha256sum -c SHA256SUMS.txt`.

## Features and controls

- **Nested tasks:** add, edit, copy, and organize tasks into multiple levels. Right-click for the task menu. Deletion has no confirmation dialog. The undo button beside the “My tasks” count reverses renames and deletions one at a time, newest first, restoring descendants when undoing a deletion. It lights up while history is available and becomes gray and disabled when history is empty. Deleting tasks opens neither a bottom notice nor the AI panel. Creating, completing, reordering, or collapsing tasks neither clears this history nor gets undone by this button.
- **Undo history:** kept in memory for the current session, up to 100 steps, with an additional limit on retained task records to bound memory use. Replacing tasks through import or undoing an entire AI turn clears the history.
- **Collapsing and ordering:** click a task title or its triangle to toggle subtasks. Drag the left handle to reorder unfinished tasks under the same parent.
- **Completion rules:** completing a parent completes all its descendants. Reopening a child reopens its ancestors. Completed tasks appear below unfinished tasks, with the most recently completed first under each parent.
- **Completed task visibility:** use the toggle beside “My tasks” to show or hide completed items.
- **Desktop controls:** dark theme, compact layout, window dragging, tray integration, and a single running instance. Choose whether the close button quits or hides the application to the tray.
- **Sticky notes:** press the configured shortcut (defaults to `F2`, with global hotkey support when unfocused and `--open-note` CLI support) or click `✍` in the header to open multiple independent, draggable sticky note windows. Type text and paste clipboard bitmaps, image files, HTML images (`data:image` / `file://`), or local image paths with `Ctrl+V` (Ubuntu also falls back to `wl-paste` / `xclip`). Note text and images stay in memory only without automatic disk persistence—clicking `✕` closes and discards the note while cleaning up temporary preview files. Click `💾` (or press `Ctrl+S`) to save the note as a `.md` file, exporting any pasted images alongside it with Markdown image links. Configure the note shortcut in Settings, and double-click any image in a note to preview it in the system's default image viewer.
- **Chinese and English:** select the interface language in Settings and save to apply immediately. Task titles, existing conversations, and poems retain their original text.
- **Poetry on startup:** each launch selects a couplet of five- or seven-character Chinese verse. A byte index allows reading one entry without loading the entire corpus into memory.
- **Local data:** tasks save automatically. Import and export JSON to transfer tasks between platforms; automatic cloud synchronization is not included.
- **Copying text:** select and copy poems, conversations, and explanatory text. Copy task titles through the context menu.

## AI companion

Click “Chat with AI” to expand the right panel. Open **AI configuration** from Settings or the chat panel gear, then enter a Chat Completions-compatible **URL, model name, and API key**. Local services without authentication can use an empty key.

- **Enter** sends, **Shift+Enter** inserts a newline, and **Ctrl+Enter** also sends.
- Stop a request or undo task changes made during the current AI turn. If other changes have occurred since then, undoing the entire turn is blocked to avoid overwriting them.
- The AI can list, create, edit, move, complete, and delete tasks. Task editing requires function/tool calling support from the service and model.
- Task information and conversation content are sent to your configured AI service as part of requests. Everyday task management works without configuring AI.

API keys stay in memory for the current session by default. If you enable “Remember key,” Windows uses DPAPI encryption and Ubuntu uses the system Secret Service keyring. Keys are never included in task JSON, and an unavailable keyring does not trigger a plaintext fallback. Conversations and sticky notes stay in memory only and are not saved to disk when closed (notes can be exported as Markdown via Save as).

## Data locations

| Platform | Tasks and preferences | Optional remembered key |
|---|---|---|
| Windows | `%LOCALAPPDATA%\YijianTodo\state.json` | `key.dpapi` in the same directory |
| Ubuntu | `$XDG_DATA_HOME/yijian-todo/state.json`, defaulting to `~/.local/share/yijian-todo/state.json` | System keyring, accessed through `secret-tool` |

Saving uses a temporary file and a previous-version backup. If a data file is damaged, the original is preserved and the backup is tried. Updating the application does not overwrite user data. Use `--data-dir /absolute/path` for a separate data directory.

## Package size

The full Ubuntu edition installs approximately 95 MB of application files, including the .NET runtime, graphics and text rendering libraries, and roughly 9 MB of poetry and its index. The Lite edition installs approximately 29 MB of application files but still requires the system .NET runtime. Download and disk sizes do not indicate RAM or CPU use; resource usage needs measurement on the target device.

## Source layout

```text
ToDo/
├── README.md                 # Chinese documentation
├── README.en.md              # English documentation
├── CHANGELOG.md              # Release changelog
├── YijianTodo.sln
├── build-release.ps1         # Four installers, checksums, and release notes
├── windows/                  # WPF interface and Windows installer
├── ubuntu/                   # Avalonia interface and Ubuntu packaging scripts
├── shared/
│   ├── Yijian.Core/          # Tasks, AI, storage, and translations
│   └── Assets/               # Icons, poetry corpus, and byte index
├── tools/                    # Windows source quick-launch scripts
└── .github/workflows/        # Build configuration for both platforms
```

Clone the entire repository: both platforms depend on `shared`. Build outputs, local state, key files, and caches are excluded by `.gitignore`.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Windows build scripts also require PowerShell 7. The first NuGet restore needs network access.

```bash
git clone https://github.com/mixwcat/ToDo.git
cd ToDo
```

### Windows application

```powershell
.\windows\build.ps1
```

This creates `windows/一件Todo.exe` and an adjacent `Assets` directory. Keep both together. This build uses the system .NET 8 Desktop Runtime x64. Exit any running instance before building.

### Ubuntu portable archive

On Ubuntu:

```bash
bash ubuntu/build.sh
# Optional: --runtime linux-arm64 or --framework-dependent
```

This creates `ubuntu/dist/yijian-todo-linux-x64/` and a matching `.tar.gz`, including the .NET runtime by default. The current GitHub Release provides `.deb` installers; portable archives are built from source.

After copying the archive to Ubuntu:

```bash
tar -xzf yijian-todo-linux-x64.tar.gz
cd yijian-todo-linux-x64
./YijianTodo
```

Keep the entire extracted directory. Run `bash install.sh` inside it to create a user-level application menu entry without sudo. It installs under `${XDG_DATA_HOME:-~/.local/share}/yijian-todo-app`.

If a desktop lacks the required libraries or Chinese fonts:

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libfreetype6 \
  libxext6 libxrender1 libxi6 libxrandr2 libxcursor1 libglib2.0-bin \
  fonts-noto-cjk libsecret-tools
```

You can also cross-publish from Windows with PowerShell 7:

```powershell
.\ubuntu\build.ps1
# Optional: -Runtime linux-arm64 or -FrameworkDependent
```

Linux execution permissions are preserved without running Ubuntu or WSL on the build machine.

### Release installers

Run from the repository root on Windows with the .NET 8 SDK, PowerShell 7, and Python 3.10+:

```powershell
.\build-release.ps1 -Version 0.1.1
```

The `releases/` directory receives four installers, `SHA256SUMS.txt`, and `RELEASE-NOTES.md`. You can also build them separately:

```powershell
.\windows\build-installer.ps1 -Version 0.1.1
.\windows\build-installer.ps1 -Version 0.1.1 -Lite
python ubuntu/build-deb.py --version 0.1.2
python ubuntu/build-deb.py --version 0.1.2 --lite
```

Run these commands sequentially because both platforms share the core project's build directories. Windows packaging uses NSIS: the script uses an available `makensis.exe`, or downloads version 3.11, verifies its SHA256, and extracts it into a temporary directory. Use `-NsisCompiler` to specify a compiler. Ubuntu `.deb` packaging uses only the Python standard library and does not require WSL. Pass `--runtime linux-arm64` to build an ARM64 package.

GitHub Actions provides builds for both platforms and accepts a version when triggered manually. Upload the four installers and `SHA256SUMS.txt` as release assets, using `RELEASE-NOTES.md` for the description. Build outputs are not committed to the source repository.

## Architecture and validation status

`shared/Yijian.Core` has no WPF or Avalonia dependency. Hosts call `TaskStore.Execute(name, JsonElement)` to change tasks, `Snapshot()` to read state, and subscribe to `Changed`. Allowed AI tools are `list_tasks`, `create_task`, `update_task`, `set_task_completed`, `move_task`, and `delete_task`; see `AiClient.Tools()` for definitions. The interface does not execute arbitrary code or file operations.

Shared translations live in `Strings.cs` and `Resources/en-US.json`. Ubuntu separates the main window, task list, chat, dialogs, and Linux platform services. General settings and AI configuration use separate panels. Lists use virtualization, chat and AI history are bounded, and the save timer runs only after changes. Single-instance activation uses local named pipes rather than a TCP port. The poetry corpus is stored as binary bytes in Git so line-ending conversion cannot invalidate its index.

Build both projects with `dotnet build YijianTodo.sln -c Release`. Both projects have been compiled on Windows. Validation includes Avalonia headless interactions with isolated data, a local mock AI service, installer integrity checks, and WSL2 (Ubuntu 24.04) verification of the Linux package startup, legacy note cleanup, and memory-only sticky note behavior. Version 0.1.1 also passes 38 shared-core and headless UI checks for repeated undo. Actual installation and uninstallation, as well as tray behavior, keyring integration, and performance on a real Ubuntu desktop, still need device testing. The user verified the v0.1.2 window dragging fix on an Ubuntu desktop.
