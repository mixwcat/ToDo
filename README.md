# 一件 Todo · Yijian Todo

简体中文 · [English](README.en.md)

一个竖排的桌面任务工具：诗句、层级任务，以及能帮忙调整清单的 AI 伙伴。提供中文和英文界面。

Windows 使用 C# / .NET 8 / WPF；Ubuntu 使用 C# / .NET 8 / Avalonia 11。两个界面独立维护，共用任务逻辑、AI 工具接口、持久化、翻译与诗库。没有浏览器或 WebView，也没有常驻轮询服务。

[下载安装包](https://github.com/mixwcat/ToDo/releases/latest) · [版本记录](https://github.com/mixwcat/ToDo/releases) · [反馈问题](https://github.com/mixwcat/ToDo/issues)

## 下载与安装

当前版本：**v0.1.0**。以下安装包均为 x86-64，普通用户推荐选择完整版。

| 系统 | 完整版：自带 .NET 运行时 | 精简版：使用系统 .NET 运行时 |
|---|---|---|
| Windows 11 x64 | [安装包 · 50.4 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.0/YijianTodo-0.1.0-windows-x64-setup.exe) | [安装包 · 3.7 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.0/YijianTodo-0.1.0-windows-x64-lite-setup.exe) |
| Ubuntu 22.04 / 24.04 amd64 | [deb · 31.4 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.0/YijianTodo-0.1.0-ubuntu-amd64.deb) | [deb · 9.5 MB](https://github.com/mixwcat/ToDo/releases/download/v0.1.0/YijianTodo-0.1.0-ubuntu-amd64-lite.deb) |

完整版和精简版选择一种安装即可。安装包不在源码仓库中，请从 GitHub Releases 下载。Windows 双击 `.exe` 安装；Ubuntu 使用 `sudo apt install ./下载的文件名.deb`，具体要求见下文。

更新或卸载前先从托盘菜单退出应用；两端卸载均保留用户任务、偏好和已记住的 Key。当前 Windows 安装包尚未进行代码签名。

Release 提供 [SHA256SUMS.txt](https://github.com/mixwcat/ToDo/releases/download/v0.1.0/SHA256SUMS.txt)。Windows 用 `Get-FileHash 文件名 -Algorithm SHA256` 对照；Ubuntu 用 `sha256sum 文件名` 对照。下载全部安装包后，也可执行 `sha256sum -c SHA256SUMS.txt`。

## 项目结构

```text
ToDo/
├── README.md
├── README.en.md             # English documentation
├── YijianTodo.sln
├── windows/                 # Windows / WPF
│   ├── build.ps1
│   └── src/Yijian.Win/
├── ubuntu/                  # Ubuntu / Avalonia
│   ├── build.sh             # 在 Ubuntu 构建
│   ├── build.ps1            # 在 Windows 跨平台发布 Linux 包
│   ├── install.sh           # 发布包中的用户级安装入口
│   └── src/Yijian.Ubuntu/
├── shared/
│   ├── Yijian.Core/         # 两端唯一的共用核心源码
│   └── Assets/              # 图标、五言七言诗库和字节索引
├── prototype/               # 原 HTML 原型
└── .github/workflows/       # Windows、Ubuntu 构建与产物归档
```

两个平台均依赖 `shared`，开发时请克隆完整仓库。`.gitignore` 已排除运行产物、构建缓存、本地状态和密钥文件。

## 功能

- 添加、修改、复制、直接删除任务，创建多层子任务；删除在主界面底部提供撤销，不弹出 AI。
- 点击任务文字或右侧三角展开、收起子任务；拖动左侧手柄调整同级未完成任务顺序。
- 完成主任务同时完成全部子任务；取消完成子任务会重新打开祖先。完成项排在未完成项之后，同级完成项按最近完成优先排列。
- “我的任务”右侧切换显示已完成任务。支持紧凑布局、深色主题、窗口拖动与托盘。
- 设置中选择简体中文或 English，保存后立即生效；保留任务、诗句、对话和草稿。任务标题与已有对话不做自动翻译。
- 每次启动从本地诗库随机读取一联五言或七言诗。通过小索引读取单条，不把整个诗库载入内存；英文界面保留诗句原文。
- “与 AI 对话”展开右侧任务伙伴。通用设置只保留“AI 配置”按钮，点击打开独立配置面板；聊天面板齿轮也可直接进入。
- 填写兼容 Chat Completions 的 URL、模型名称和 Key。AI 服务需支持 function/tool calling 才能调整任务；本地无鉴权服务可留空 Key。
- Enter 发送，Shift+Enter 换行，Ctrl+Enter 也发送；配置缺失时保留草稿。支持停止请求、AI 修改后的本轮撤销；若已有其他修改，阻止整轮撤销以免覆盖新内容。
- 诗句、对话及说明文字可拖选复制；按钮与菜单标签保持正常点击。任务标题通过右键菜单复制。
- 任务自动保存，支持 JSON 导入导出。两端任务文件兼容，可通过导出、导入迁移；不会自动云同步。

AI 按请求将任务清单和对话内容发送到你配置的服务；日常任务管理无需配置 AI。

## Windows

从 [GitHub Releases](https://github.com/mixwcat/ToDo/releases/latest) 下载安装包：

| 安装包 | 使用方式 |
|---|---|
| `YijianTodo-0.1.0-windows-x64-setup.exe` | 完整版，自带 .NET，推荐普通用户使用 |
| `YijianTodo-0.1.0-windows-x64-lite-setup.exe` | 精简版，需要系统已安装 .NET 8 Desktop Runtime x64 |

双击安装，为当前用户创建开始菜单、桌面入口和卸载项。安装位置默认是 `%LOCALAPPDATA%\Programs\YijianTodo`，无需管理员权限。升级和卸载前先从托盘退出；卸载保留 AppData 中的任务和 Key。当前生成的安装包没有代码签名。

精简版需要 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0/runtime)，完整版自带运行时。自行构建的 `windows/一件Todo.exe` 也使用系统运行时，需保留旁边的 `Assets` 文件夹。

开发需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 和 PowerShell 7，首次还原 NuGet 依赖需要网络连接。克隆后在项目根目录运行：

```bash
git clone https://github.com/mixwcat/ToDo.git
cd ToDo
```

```powershell
.\windows\build.ps1
```

构建前从托盘菜单退出应用。脚本将托管程序集打包进 `windows/一件Todo.exe`，同时复制运行资源，成功后清理构建缓存。已有 exe 为本地运行产物，不提交到源码仓库。

设置可选择 × 是退出还是隐藏到托盘。再次启动会恢复已有窗口；托盘右键菜单可彻底退出。

## Ubuntu

目标为 Ubuntu 22.04 / 24.04 桌面，默认 x86-64。X11 可直接运行；Wayland 桌面通过 XWayland 运行。ARM64 可通过构建参数单独发布。

### 安装 .deb

从 [GitHub Releases](https://github.com/mixwcat/ToDo/releases/latest) 下载 `YijianTodo-0.1.0-ubuntu-amd64.deb`，自带 .NET 运行时，在下载目录执行：

```bash
sudo apt install ./YijianTodo-0.1.0-ubuntu-amd64.deb
```

需要共用系统 .NET 时选择 `YijianTodo-0.1.0-ubuntu-amd64-lite.deb`。它声明 `dotnet-runtime-8.0` 依赖，需要系统软件源提供该包；Ubuntu 上使用的是 .NET Runtime，不需要 Windows Desktop Runtime。完整版和精简版是同一个软件包的两种构建，选择一种安装即可。

安装后从应用菜单启动，或执行 `yijian-todo`。软件安装在 `/usr/lib/yijian-todo`，任务仍保存在用户数据目录。卸载使用 `sudo apt remove yijian-todo`，保留用户任务及密钥环记录。

`.deb` 会声明必要的系统库，Ubuntu 桌面通常已有大部分依赖；中文字体 `fonts-noto-cjk` 和记住 Key 所需的 `libsecret-tools` 是推荐项，缺少时 apt 可能额外安装。当前 `.deb` 完整版约 31.4 MB，精简版约 9.5 MB；这是安装包体积，运行内存需在目标设备实测。

### 使用发布包

便携包通过下文的源码构建脚本生成，当前 Release 提供的是 `.deb` 安装包。将自行构建的 `ubuntu/dist/yijian-todo-linux-x64.tar.gz` 复制到 Ubuntu 后：

```bash
tar -xzf yijian-todo-linux-x64.tar.gz
cd yijian-todo-linux-x64
./YijianTodo
```

默认发布包包含 .NET 运行时，无需另装 .NET。整个解压目录需要一起保留，尤其是本机库和 `Assets`。

要从应用菜单启动，可在解压目录执行：

```bash
bash install.sh
```

安装到 `${XDG_DATA_HOME:-~/.local/share}/yijian-todo-app`，并创建用户级应用菜单入口；无需 sudo。下次更新时先退出应用，再用新包执行安装脚本。

如果桌面系统缺少运行库或中文字体，可安装：

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libfreetype6 \
  libxext6 libxrender1 libxi6 libxrandr2 libxcursor1 libglib2.0-bin \
  fonts-noto-cjk libsecret-tools
```

GNOME 托盘依赖 AppIndicator / KStatusNotifier 支持。Ubuntu 默认桌面通常已具备；若没有可用托盘，选择“最小化到托盘”会退回任务栏最小化，避免隐藏后无法找回。单实例激活通过本地命名管道完成，不开放 TCP 端口。

### 从源码构建

安装 .NET 8 SDK 后，在项目根目录执行：

```bash
bash ubuntu/build.sh
```

生成 `ubuntu/dist/yijian-todo-linux-x64/` 与同名 `.tar.gz`。可选参数：

```bash
bash ubuntu/build.sh --runtime linux-arm64
bash ubuntu/build.sh --framework-dependent
```

`--framework-dependent` 使用系统 .NET 8，发布包更小；默认随包携带运行时，便于直接运行。打包大小与应用实际内存占用是两回事，运行资源占用需要在目标桌面实测。

在 Windows / PowerShell 7 中也可生成 Ubuntu 发布包：

```powershell
.\ubuntu\build.ps1
# 可选：-Runtime linux-arm64 或 -FrameworkDependent
```

跨平台打包保留 Linux 执行权限，不需要在 Windows 安装或启动 Ubuntu。

## 制作 GitHub Release 安装包

Windows 安装器使用 NSIS，Ubuntu 安装器使用标准 Debian 包格式。以下命令在 Windows 根目录执行，需要 .NET 8 SDK、PowerShell 7 和 Python 3.10+：

```powershell
.\build-release.ps1 -Version 0.1.0
```

它顺序生成四个安装包、校验文件和发布说明。也可分别构建：

```powershell
.\windows\build-installer.ps1 -Version 0.1.0
.\windows\build-installer.ps1 -Version 0.1.0 -Lite
python ubuntu/build-deb.py --version 0.1.0
python ubuntu/build-deb.py --version 0.1.0 --lite
```

请顺序执行，因为两个平台共用核心项目的构建目录。Windows 脚本优先使用已有的 `makensis.exe`；否则从 NSIS 官方下载固定版本 3.11 并校验 SHA-256，只解压到系统临时目录，不安装到系统。也可用 `-NsisCompiler /path/to/makensis.exe` 指定编译器。Ubuntu `.deb` 构建只使用 Python 标准库，不需要 WSL 或第三方 Python 包；`--runtime linux-arm64` 可生成 ARM64 包。

将 `releases` 中的四个安装包和 `SHA256SUMS.txt` 放入 GitHub Release，说明可参考同目录的 `RELEASE-NOTES.md`。构建产物不进入源码提交；GitHub Actions 已配置两端安装包构建，可手动指定版本号。

完整包包含私有 .NET 运行时，方便直接运行；精简包共用系统 .NET。Ubuntu 完整版安装后约 95 MB，包含 .NET 运行时、图形及文字渲染库，以及约 9 MB 的诗库和索引；精简版应用文件约 29 MB，但仍需要系统提供 .NET。两端采用相同方式打包比较体积才有意义；包大不等于运行时内存或 CPU 占用大。

## 数据与 AI Key

| 平台 | 任务与偏好 | 可选记住的 Key |
|---|---|---|
| Windows | `%LOCALAPPDATA%\YijianTodo\state.json` | 同目录 `key.dpapi`，Windows DPAPI 加密 |
| Ubuntu | `$XDG_DATA_HOME/yijian-todo/state.json`，默认 `~/.local/share/yijian-todo/state.json` | 系统 Secret Service 密钥环，经 `secret-tool` 访问 |

默认仅在本次运行中使用 Key。Ubuntu 选择记住 Key 时需要 `libsecret-tools` 和已解锁的桌面密钥环；不会将 Key 降级写入明文文件。两端的 JSON 均不包含 Key。对话仅在当前运行期间保留，关闭应用后不写入磁盘。

保存使用临时文件和上次备份；原始损坏文件保留后尝试读取备份。应用目录迁移或更新不覆盖用户目录中的任务。可用 `--data-dir /absolute/path` 指定独立数据目录。

## 架构与验证

`shared/Yijian.Core` 不依赖 WPF 或 Avalonia。平台宿主通过 `TaskStore.Execute(name, JsonElement)` 操作任务，`Snapshot()` 读取、`Changed` 订阅变化。工具白名单为 `list_tasks`、`create_task`、`update_task`、`set_task_completed`、`move_task`、`delete_task`，参数见 `AiClient.Tools()`。接口不执行代码或任意文件操作。

共用翻译位于 `Strings.cs` 和 `Resources/en-US.json`。Ubuntu 按主界面、任务、聊天、对话框和 Linux 平台服务分文件；设置与 AI 配置分开。列表采用虚拟化，聊天及 AI 历史有上限，保存计时器只在修改后运行。

可用 `dotnet build YijianTodo.sln -c Release` 编译两端；根目录 GitHub Actions 分别生成 Windows 与 Ubuntu 构建产物。诗库文本按二进制字节保存，避免 Git 换行转换破坏字节索引。

当前已在 Windows 编译两个平台项目，完成隔离数据下的 Avalonia 无界面交互、本地模拟 AI 检查及安装包完整性校验。实际安装、卸载，以及真实 Ubuntu 桌面的窗口管理、托盘、密钥环和性能仍待实机验证。
