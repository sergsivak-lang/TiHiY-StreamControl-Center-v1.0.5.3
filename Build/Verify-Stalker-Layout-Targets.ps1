param(
    [string]$ProjectDir = '.'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path $ProjectDir).Path
python (Join-Path $project 'Build\Verify-Stalker-Layout-Targets.py') `
    --xaml (Join-Path $project 'MainWindow.xaml') `
    --runtime (Join-Path $project 'Services\StalkerApprovedLayoutRuntime.cs')
if ($LASTEXITCODE -ne 0) { throw 'Grid-based STALKER layout verification failed.' }
