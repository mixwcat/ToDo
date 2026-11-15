param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0',
    [switch]$MetadataOnly
)
$ErrorActionPreference = 'Stop'
if (-not $MetadataOnly) {
    & (Join-Path $PSScriptRoot 'windows/build-installer.ps1') -Version $Version
    & (Join-Path $PSScriptRoot 'windows/build-installer.ps1') -Version $Version -Lite
    python (Join-Path $PSScriptRoot 'ubuntu/build-deb.py') --version $Version
    if ($LASTEXITCODE -ne 0) { throw 'Ubuntu package build failed' }
    python (Join-Path $PSScriptRoot 'ubuntu/build-deb.py') --version $Version --lite
    if ($LASTEXITCODE -ne 0) { throw 'Ubuntu Lite package build failed' }
}
$todoReleases = Join-Path $PSScriptRoot 'releases'
$todoFiles = @(
    "YijianTodo-$Version-windows-x64-setup.exe",
    "YijianTodo-$Version-windows-x64-lite-setup.exe",
    "YijianTodo-$Version-ubuntu-amd64.deb",
    "YijianTodo-$Version-ubuntu-amd64-lite.deb"
)
$todoHashes = foreach ($todoName in $todoFiles) {
    $todoPath = Join-Path $todoReleases $todoName
    if (-not (Test-Path -LiteralPath $todoPath)) { throw "Missing package: $todoName" }
    (Get-FileHash -LiteralPath $todoPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $todoName
}
[IO.File]::WriteAllLines((Join-Path $todoReleases 'SHA256SUMS.txt'), $todoHashes, [Text.UTF8Encoding]::new($false))
$todoSizes = foreach ($todoName in $todoFiles) {
    ((Get-Item -LiteralPath (Join-Path $todoReleases $todoName)).Length / 1000000).ToString('0.0',[Globalization.CultureInfo]::InvariantCulture)
}
$todoNotes = @'
# Yijian Todo __VERSION__

Windows 11 与 Ubuntu 22.04/24.04 桌面版，支持中英文、层级任务、拖拽排序、完成联动、诗句、深色主题、托盘、独立 AI 配置及任务操作工具。

| 系统 | 文件 | 下载大小 | .NET 要求 |
|---|---|---|---|
| Windows x64 | `YijianTodo-__VERSION__-windows-x64-setup.exe` | __WIN_FULL__ MB | 自带运行时，推荐 |
| Windows x64 精简版 | `YijianTodo-__VERSION__-windows-x64-lite-setup.exe` | __WIN_LITE__ MB | 需要 .NET 8 Desktop Runtime x64 |
| Ubuntu amd64 | `YijianTodo-__VERSION__-ubuntu-amd64.deb` | __UBUNTU_FULL__ MB | 自带运行时，推荐 |
| Ubuntu amd64 精简版 | `YijianTodo-__VERSION__-ubuntu-amd64-lite.deb` | __UBUNTU_LITE__ MB | 需要系统 dotnet-runtime-8.0 |

Windows：双击安装，为当前用户创建开始菜单与桌面入口，无需管理员权限。

Ubuntu：`sudo apt install ./YijianTodo-__VERSION__-ubuntu-amd64.deb`，安装后从应用菜单或运行 `yijian-todo` 启动。精简版安装方法相同，apt 需要可用的 .NET 8 软件源。缺少的系统库和推荐字体可能额外占用磁盘。

完整与精简版本选择一种即可。升级前从托盘退出应用；卸载保留用户任务和密钥。AI Key 默认只在当前运行中保留，可选使用系统加密存储或密钥环记住。

校验：Linux 可使用 `sha256sum -c SHA256SUMS.txt`，Windows 可用 `Get-FileHash 文件名 -Algorithm SHA256` 对照。

当前 Windows 安装包未进行代码签名；已完成构建及包内容校验，安装、卸载以及真实 Ubuntu 桌面行为仍需实机测试。
'@
$todoNotes = $todoNotes.Replace('__VERSION__',$Version).Replace('__WIN_FULL__',$todoSizes[0]).Replace('__WIN_LITE__',$todoSizes[1]).Replace('__UBUNTU_FULL__',$todoSizes[2]).Replace('__UBUNTU_LITE__',$todoSizes[3])
[IO.File]::WriteAllText((Join-Path $todoReleases 'RELEASE-NOTES.md'), $todoNotes + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Ready: $todoReleases"
