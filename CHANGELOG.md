# 版本记录 · Changelog

## v0.1.3 · 2026-11-19

### 简体中文

- Windows 与 Ubuntu 同步新增**小便签**功能：按 `F2`（支持失焦全局快捷键与 `--open-note` 参数）或点击顶部 `✍` 按钮即可打开小便签，支持同时打开多个可自由拖拽、缩放的便签窗口。
- 便签内支持输入多行文字与 `Ctrl+V` 粘贴剪贴板位图、图片文件、HTML 图片（`data:image` / `file://`）或本地图片路径；Ubuntu 端额外支持通过 `wl-paste` / `xclip` 读取 Linux 剪贴板图片。
- 便签文本与粘贴图片仅在内存中保留，关闭即销毁，不再将便签内容或图片自动写入本地数据目录；启动时会自动清理历史残留的 `notes` / `noteImageDir` 字段与 `note-images` 目录。
- 便签标题栏新增 `💾`（**另存为**）按钮并支持 `Ctrl+S` 快捷键，可将便签导出保存为 `.md` 文件，同时将便签内图片导出到同级目录并自动生成 Markdown 图片引用。
- 偏好设置支持“打开便签快捷键”按键录制；双击便签中的图片即可生成临时预览文件并调用系统默认图片查看器打开，移除图片或关闭便签时自动清理临时文件。

### English

- Added **Sticky Notes** to both Windows and Ubuntu: press `F2` (with global hotkey support when unfocused and `--open-note` CLI argument) or click the `✍` header button to open multiple independent, draggable, and resizable sticky note windows.
- Sticky notes support multi-line text input and `Ctrl+V` image pasting from clipboard bitmaps, copied image files, HTML images (`data:image` / `file://`), or local image paths; Ubuntu also includes a `wl-paste` / `xclip` fallback for Linux clipboard images.
- Sticky note text and pasted images stay in memory only and are discarded on close without saving to disk; legacy `notes` / `noteImageDir` fields in `state.json` and `note-images` directories are cleaned up automatically on load.
- Added a `💾` (**Save as**) header button and `Ctrl+S` shortcut to export sticky notes as Markdown (`.md`) files, writing any pasted images alongside the `.md` file with generated Markdown image links.
- Preferences support shortcut key recording for opening notes; double-clicking an image creates a temporary preview file and opens it in the system's default image viewer, with temporary files automatically deleted when removing the image or closing the note.

## v0.1.2 · 2026-11-15 · Linux

### 简体中文

- 修复 Ubuntu 无边框窗口无法从顶部标题或空白区域拖动的问题：空白区域接收鼠标事件，顶部标题不再作为可选中文字处理。
- 正文仍支持选择复制，按钮、任务列表和对话框沿用原有交互。
- Debian 安装包增加 ICU 76、78 依赖候选，兼容 Ubuntu 26.04 使用的 ICU 78。
- 本次仅发布 Linux 完整版和精简版；Windows 下载仍为 v0.1.1。

验证：Linux Release 编译通过（0 警告、0 错误），用户已完成 Ubuntu 窗口拖动测试。两个安装包均通过文件完整性与隔离数据启动检查；完整版通过 Ubuntu 26.04 安装依赖模拟检查。

### English

- Fixed dragging the borderless Ubuntu window by its title and empty areas. Empty areas now participate in pointer hit testing, and the title uses non-selectable text.
- Body text remains selectable; buttons, task lists, and dialogs retain their existing input handling.
- Added ICU 76 and 78 alternatives to Debian package dependencies, including ICU 78 used by Ubuntu 26.04.
- This release contains Linux full and Lite installers only. Windows downloads remain at v0.1.1.

Validation: Linux Release build passed with zero warnings or errors; the user verified window dragging on Ubuntu. Both installers passed file integrity and isolated-data startup checks. The full edition passed an Ubuntu 26.04 dependency-resolution simulation.

## v0.1.1 · 2026-11-15

### 简体中文

- 撤销按钮移至“我的任务”数量右侧，默认灰色禁用，有可撤销历史时亮起；删除不再显示底部撤销提示框。
- 重命名和删除支持按倒序连续撤销；撤销删除会恢复任务及其全部子任务。
- 新增、完成、排序、展开或收起任务不会清空历史，撤销重命名或删除不会覆盖这些后续操作。
- Windows 与 Ubuntu 共用撤销逻辑，仅记录旧名称和被删任务，避免每一步保存整份任务快照。
- 历史仅在当前运行中保留，最多 100 步，并限制保留的任务记录数量；导入替换清单或撤销整个 AI 修改轮次时清空历史。
- 更新中英文说明及安装包下载入口。

验证：两端编译通过，38 项共用核心与 Avalonia 无界面交互检查通过；四个安装包在发布前进行完整性校验。实际安装、卸载及真实 Ubuntu 桌面行为仍待实机验证。Windows 安装包尚未签名。

### English

- Moved undo beside the “My tasks” count. It is gray and disabled when no history is available, and highlighted when there is something to undo. Deletion no longer shows a bottom undo notice.
- Renames and deletions can be undone repeatedly, newest first. Undoing a deletion restores the task and all its descendants.
- Creating, completing, reordering, expanding, or collapsing tasks preserves history. Undoing a rename or deletion preserves those subsequent changes.
- Windows and Ubuntu share the history implementation. Entries retain old titles or deleted tasks instead of a full task-list snapshot for every step.
- History lasts for the current session, up to 100 steps, with an additional retained-record limit. Import replacement and undoing an entire AI turn clear history.
- Updated Chinese and English documentation and installer links.

Validation: both projects compile, and 38 shared-core and Avalonia headless interaction checks pass. All four installers receive integrity checks before publication. Actual installation, uninstallation, and behavior on a real Ubuntu desktop still need device testing. Windows installers are unsigned.

## v0.1.0 · 2026-11-15

- 首次发布 Windows 和 Ubuntu 完整版、精简版安装包，提供中英文界面、层级任务、诗句、托盘及 AI 任务伙伴。
- Initial Windows and Ubuntu release with full and Lite installers, Chinese and English interfaces, nested tasks, poetry, tray integration, and an AI companion.
