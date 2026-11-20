# Build the Windows app straight from source and launch it - no installer or packaging step.
# Used by the desktop shortcut "Yijian Todo (from source)" through launch-hidden.vbs.
# Keep this file ASCII-only: Windows PowerShell may otherwise read it with the ANSI code page.

$ErrorActionPreference = 'Stop'
# English build output keeps the log readable regardless of the console code page.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_NOLOGO = '1'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$repo    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'windows\src\Yijian.Win\Yijian.Win.csproj'
$outDir  = Join-Path $repo 'windows\src\Yijian.Win\bin\Release\net8.0-windows'
$logPath = Join-Path $env:TEMP 'yijian-run-from-source.log'

function Show-Failure([string]$message) {
    Write-Host $message
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [void][System.Windows.Forms.MessageBox]::Show($message, 'Yijian Todo', 'OK', 'Error')
    } catch {
        Start-Process notepad.exe $logPath -ErrorAction SilentlyContinue
    }
}

try {
    # Close a running copy first, so the binaries just built are what actually shows up.
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like '*Todo*' -and $_.Path -and
        $_.Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($proc in $running) {
        $null = $proc.CloseMainWindow()
        Start-Sleep -Milliseconds 700
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    }
    if ($running.Count -gt 0) { Start-Sleep -Milliseconds 500 }

    $buildOutput = & dotnet build $project -c Release --nologo -v minimal 2>&1 | Out-String
    Set-Content -LiteralPath $logPath -Value $buildOutput -Encoding UTF8
    if ($LASTEXITCODE -ne 0) {
        Show-Failure "Build failed (exit code $LASTEXITCODE).`n`nFull log:`n$logPath"
        exit 1
    }

    $exe = Get-ChildItem -LiteralPath $outDir -Filter '*.exe' -File -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $exe) {
        Show-Failure "Build finished but no executable was found in:`n$outDir`n`nLog:`n$logPath"
        exit 1
    }

    Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName
} catch {
    Show-Failure "Could not start Yijian Todo from source.`n`n$($_.Exception.Message)`n`nLog:`n$logPath"
    exit 1
}
