#requires -Version 5
# Smoke test: runs every payload x target combination in BOTH injection modes
#   - launch: the injector creates the target suspended, injects, resumes
#   - attach: the target is already running and the injector attaches via --pid
# Each scenario is verified by the marker + message the payload writes to
# %TEMP%\reloaded_inject_demo.log.

$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$log  = Join-Path $env:TEMP 'reloaded_inject_demo.log'

if (-not (Test-Path (Join-Path $dist 'Injector.exe'))) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE) { throw "build failed" }
}

$payloads = @(
    @{ Name = 'native  payload'; Dll = 'native_payload.dll'; Export = $null;  Tag = '[NATIVE]'  }
    @{ Name = 'managed payload'; Dll = 'ManagedPayload.dll'; Export = 'Run';  Tag = '[MANAGED]' }
)
$targets = @(
    @{ Name = 'native  target'; Exe = 'native_target.exe' }
    @{ Name = 'managed target'; Exe = 'ManagedTarget.exe' }
)

$script:failed = 0

function Test-Log([string] $tag, [string] $msg) {
    (Test-Path $log) -and (Select-String -Path $log -SimpleMatch $tag -Quiet) `
                     -and (Select-String -Path $log -SimpleMatch $msg -Quiet)
}

function Report([string] $name, [bool] $ok) {
    if ($ok) { Write-Host "PASS  $name" -ForegroundColor Green }
    else     { Write-Host "FAIL  $name" -ForegroundColor Red; $script:failed++ }
}

Push-Location $dist
try {
    foreach ($t in $targets) {
        foreach ($p in $payloads) {
            $exportArgs = if ($p.Export) { @('--export', $p.Export) } else { @() }

            # --- launch mode: injector creates the target suspended, injects, resumes ---
            $msg = "launch-$($t.Exe)-$($p.Dll)"
            Remove-Item $log -ErrorAction SilentlyContinue
            & .\Injector.exe @($t.Exe, $p.Dll) @exportArgs '--msg' $msg | Out-Null
            Report "launch  $($p.Name) -> $($t.Name)" (Test-Log $p.Tag $msg)

            # --- attach mode: start the target, then inject into it by --pid ---
            $msg = "attach-$($t.Exe)-$($p.Dll)"
            Remove-Item $log -ErrorAction SilentlyContinue
            $proc = Start-Process -FilePath ".\$($t.Exe)" -PassThru
            try {
                Start-Sleep -Milliseconds 900   # let the target finish initialising
                & .\Injector.exe '--pid' $proc.Id $p.Dll @exportArgs '--msg' $msg | Out-Null
                Start-Sleep -Milliseconds 600   # let a DllMain worker thread write
                Report "attach  $($p.Name) -> $($t.Name)" (Test-Log $p.Tag $msg)
            }
            finally {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
finally { Pop-Location }

if ($script:failed) { Write-Host "`n$($script:failed) scenario(s) failed" -ForegroundColor Red; exit 1 }
Write-Host "`nall scenarios passed" -ForegroundColor Green
exit 0
