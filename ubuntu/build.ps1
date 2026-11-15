param(
    [ValidateSet('linux-x64','linux-arm64')][string]$Runtime = 'linux-x64',
    [switch]$FrameworkDependent
)
# Cross-publish from PowerShell 7; the archive records executable Unix permissions.
$ErrorActionPreference = 'Stop'
$todoUbuntuRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$todoStage = Join-Path ([IO.Path]::GetTempPath()) ('Yijian-linux-publish-' + [Guid]::NewGuid().ToString('N'))
$todoPackage = "yijian-todo-$Runtime"
try {
    dotnet publish (Join-Path $todoUbuntuRoot 'src/Yijian.Ubuntu') -c Release -r $Runtime --self-contained (-not $FrameworkDependent).ToString().ToLowerInvariant() -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $todoStage --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Linux publish failed' }
    Copy-Item -LiteralPath (Join-Path $todoUbuntuRoot 'install.sh') -Destination $todoStage
    $todoDist = Join-Path $todoUbuntuRoot 'dist'
    New-Item -ItemType Directory -Path $todoDist -Force | Out-Null
    $todoArchive = Join-Path $todoDist "$todoPackage.tar.gz"
    $todoOutput = [IO.File]::Create($todoArchive)
    $todoGzip = [IO.Compression.GZipStream]::new($todoOutput, [IO.Compression.CompressionLevel]::Optimal)
    $todoTar = [System.Formats.Tar.TarWriter]::new($todoGzip, $true)
    try {
        foreach ($todoFile in Get-ChildItem -LiteralPath $todoStage -Recurse -File) {
            $todoRelative = [IO.Path]::GetRelativePath($todoStage, $todoFile.FullName).Replace('\','/')
            $todoEntry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, "$todoPackage/$todoRelative")
            $todoEntry.Mode = [IO.UnixFileMode]$(if ($todoRelative -in @('YijianTodo','install.sh')) {493} else {420})
            $todoEntry.DataStream = [IO.File]::OpenRead($todoFile.FullName)
            try { $todoTar.WriteEntry($todoEntry) } finally { $todoEntry.DataStream.Dispose() }
        }
    } finally { $todoTar.Dispose(); $todoGzip.Dispose(); $todoOutput.Dispose() }
    Write-Host "Ready: $todoArchive"
} finally {
    $todoTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($todoStage.StartsWith($todoTempRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $todoStage -Leaf).StartsWith('Yijian-linux-publish-')) {
        if (Test-Path -LiteralPath $todoStage) { Remove-Item -LiteralPath $todoStage -Recurse -Force }
    }
}
