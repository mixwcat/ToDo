# 版本记录 · Changelog

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
