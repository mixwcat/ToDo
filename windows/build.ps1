$ErrorActionPreference = 'Stop'
$todoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$todoRepository = [IO.Path]::GetFullPath((Join-Path $todoRoot '..'))
$todoStage = Join-Path ([IO.Path]::GetTempPath()) ('Yijian-publish-' + [Guid]::NewGuid().ToString('N'))
Push-Location $todoRoot
try {
    $todoExe = Join-Path $todoRoot '一件Todo.exe'
    $todoRunning = Get-Process | Where-Object { $_.Path -eq $todoExe }
    if ($todoRunning) { throw 'Quit Yijian Todo from the tray menu before building.' }
    if (Test-Path -LiteralPath $todoExe) {
        try { $todoLock = [IO.File]::Open($todoExe, 'Open', 'ReadWrite', 'None'); $todoLock.Dispose() }
        catch { throw 'Quit Yijian Todo from the tray menu before building.' }
    }
    dotnet publish src/Yijian.Win -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $todoStage --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath (Join-Path $todoStage '一件Todo.exe') -Destination $todoExe -Force
    Copy-Item -LiteralPath (Join-Path $todoStage 'Assets') -Destination $todoRoot -Recurse -Force
    # Only the published executable and runtime assets stay beside the source.
    foreach ($todoProject in @('../shared/Yijian.Core', 'src/Yijian.Win')) {
        foreach ($todoCache in @('bin', 'obj')) {
            $todoCachePath = [IO.Path]::GetFullPath((Join-Path $todoRoot "$todoProject/$todoCache"))
            if (-not $todoCachePath.StartsWith($todoRepository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path' }
            if (Test-Path -LiteralPath $todoCachePath) { Remove-Item -LiteralPath $todoCachePath -Recurse -Force }
        }
    }
    Write-Host "Ready: $todoExe"
} finally {
    Pop-Location
    $todoTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($todoStage.StartsWith($todoTempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $todoStage -Leaf).StartsWith('Yijian-publish-')) {
        if (Test-Path -LiteralPath $todoStage) { Remove-Item -LiteralPath $todoStage -Recurse -Force }
    }
}
