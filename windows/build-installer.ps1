param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.1',
    [switch]$Lite,
    [string]$NsisCompiler
)
$ErrorActionPreference = 'Stop'
$todoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$todoRepo = [IO.Path]::GetFullPath((Join-Path $todoRoot '..'))
$todoStage = Join-Path ([IO.Path]::GetTempPath()) ('Yijian-windows-installer-' + [Guid]::NewGuid().ToString('N'))
try {
    if (-not $NsisCompiler) {
        $todoInstalled = Get-Command makensis.exe -ErrorAction SilentlyContinue
        if ($todoInstalled) { $NsisCompiler = $todoInstalled.Source }
        else {
            $todoToolRoot = Join-Path ([IO.Path]::GetTempPath()) 'yijian-release-tools'
            $NsisCompiler = Join-Path $todoToolRoot 'nsis-3.11/makensis.exe'
            if (-not (Test-Path -LiteralPath $NsisCompiler)) {
                $todoToolArchive = Join-Path ([IO.Path]::GetTempPath()) ('Yijian-nsis-' + [Guid]::NewGuid().ToString('N') + '.zip')
                Invoke-WebRequest 'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.11/nsis-3.11.zip' -OutFile $todoToolArchive
                if ((Get-FileHash -LiteralPath $todoToolArchive -Algorithm SHA256).Hash -ne 'C7D27F780DDB6CFFB4730138CD1591E841F4B7EDB155856901CDF5F214394FA1') { throw 'NSIS archive checksum mismatch' }
                Expand-Archive -LiteralPath $todoToolArchive -DestinationPath $todoToolRoot -Force
            }
        }
    }
    $todoApp = Join-Path $todoStage 'app'
    dotnet publish (Join-Path $todoRoot 'src/Yijian.Win') -c Release -r win-x64 --self-contained (-not $Lite).ToString().ToLowerInvariant() -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $todoApp --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
    $todoManifest = Join-Path $todoStage 'uninstall-files.nsh'
    $todoLines = [Collections.Generic.List[string]]::new()
    foreach ($todoFile in Get-ChildItem -LiteralPath $todoApp -Recurse -File) {
        $todoRelative = [IO.Path]::GetRelativePath($todoApp, $todoFile.FullName).Replace('$', '$$')
        $todoLines.Add('Delete "$INSTDIR\' + $todoRelative + '"')
    }
    foreach ($todoDir in Get-ChildItem -LiteralPath $todoApp -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending) {
        $todoRelative = [IO.Path]::GetRelativePath($todoApp, $todoDir.FullName).Replace('$', '$$')
        $todoLines.Add('RMDir "$INSTDIR\' + $todoRelative + '"')
    }
    [IO.File]::WriteAllLines($todoManifest, $todoLines, [Text.UTF8Encoding]::new($true))
    $todoDist = Join-Path $todoRepo 'releases'
    New-Item -ItemType Directory -Path $todoDist -Force | Out-Null
    $todoVariant = if ($Lite) {'-lite'} else {''}
    $todoInstaller = Join-Path $todoDist "YijianTodo-$Version-windows-x64$todoVariant-setup.exe"
    $todoSize = [int][Math]::Ceiling((Get-ChildItem -LiteralPath $todoApp -Recurse -File | Measure-Object Length -Sum).Sum / 1024)
    $todoArgs = @('/V2','/WX','/INPUTCHARSET','UTF8',"/DVERSION=$Version","/DAPP_DIR=$todoApp","/DOUTPUT_FILE=$todoInstaller","/DUNINSTALL_LIST=$todoManifest","/DICON_FILE=$(Join-Path $todoRepo 'shared/Assets/app.ico')","/DAPP_SIZE_KB=$todoSize")
    if ($Lite) { $todoArgs += '/DLITE' }
    $todoArgs += (Join-Path $todoRoot 'installer.nsi')
    & $NsisCompiler @todoArgs
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
    Write-Host "Ready: $todoInstaller"
} finally {
    $todoTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($todoStage.StartsWith($todoTemp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $todoStage -Leaf).StartsWith('Yijian-windows-installer-')) {
        if (Test-Path -LiteralPath $todoStage) { Remove-Item -LiteralPath $todoStage -Recurse -Force }
    }
}
