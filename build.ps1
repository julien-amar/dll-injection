#requires -Version 5
# Builds every Reloaded injection example into .\dist
#
# Native linking (Rust-MSVC and .NET NativeAOT) needs the Visual Studio
# developer environment, so this script locates and enters it automatically.

# Native tools (cargo, dotnet) write progress to stderr; keep that from aborting
# the script and instead gate on $LASTEXITCODE after each native command.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

# --- locate the VS developer environment --------------------------------------
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found - install Visual Studio with the C++ workload" }
$vcvars = & $vswhere -latest -prerelease -find 'VC\Auxiliary\Build\vcvars64.bat'
if (-not $vcvars) { throw "vcvars64.bat not found - install the 'Desktop development with C++' workload" }
$installerDir = Split-Path $vswhere -Parent

# --- tools --------------------------------------------------------------------
$cargo = "$env:USERPROFILE\.cargo\bin\cargo.exe"
if (-not (Test-Path $cargo)) { $cargo = 'cargo' }  # fall back to PATH

# Start from a clean dist so renamed/removed artifacts never linger.
if (Test-Path $dist) { Remove-Item (Join-Path $dist '*') -Recurse -Force -ErrorAction Stop }
New-Item -ItemType Directory -Force $dist -ErrorAction Stop | Out-Null

$rustTarget = 'x86_64-pc-windows-msvc'   # pin arch so it can't silently mismatch

Write-Host "==> Building Rust native target + payload ($rustTarget)" -ForegroundColor Cyan
& $cargo build --release --target $rustTarget --manifest-path (Join-Path $root 'native\Cargo.toml')
if ($LASTEXITCODE) { throw "cargo build failed" }

Write-Host "==> Building .NET injector + managed target" -ForegroundColor Cyan
dotnet build (Join-Path $root 'src\Injector\Injector.csproj') -c Release --nologo -v minimal
if ($LASTEXITCODE) { throw "injector build failed" }
dotnet build (Join-Path $root 'src\ManagedTarget\ManagedTarget.csproj') -c Release --nologo -v minimal
if ($LASTEXITCODE) { throw "managed target build failed" }

Write-Host "==> Publishing .NET managed payload (NativeAOT) in VS dev env" -ForegroundColor Cyan
$payloadProj = Join-Path $root 'src\ManagedPayload'
# Enter vcvars, expose vswhere on PATH, neutralise the Platform var vcvars sets.
cmd /c "`"$vcvars`" >nul 2>&1 && set `"PATH=%PATH%;$installerDir`" && set `"Platform=`" && dotnet publish `"$payloadProj`" -c Release --nologo -v minimal"
if ($LASTEXITCODE) { throw "managed payload publish failed" }

Write-Host "==> Assembling .\dist" -ForegroundColor Cyan
Copy-Item (Join-Path $root 'src\Injector\bin\Release\net8.0\*') $dist -Recurse -Force -ErrorAction Stop
Copy-Item (Join-Path $root 'src\ManagedTarget\bin\Release\net8.0\*') $dist -Recurse -Force -ErrorAction Stop
Copy-Item (Join-Path $root "native\target\$rustTarget\release\native_target.exe") $dist -Force -ErrorAction Stop
Copy-Item (Join-Path $root "native\target\$rustTarget\release\native_payload.dll") $dist -Force -ErrorAction Stop

$payloadDll = Get-ChildItem (Join-Path $payloadProj 'bin') -Recurse -Filter 'ManagedPayload.dll' |
    Where-Object { $_.FullName -match '[\\/]publish[\\/]' } | Select-Object -First 1 -ExpandProperty FullName
if (-not $payloadDll) { throw "published ManagedPayload.dll (NativeAOT) not found" }
Copy-Item $payloadDll $dist -Force -ErrorAction Stop

Write-Host "`nDone. Artifacts in $dist" -ForegroundColor Green
Get-ChildItem $dist | Where-Object { $_.Name -match '\.(exe|dll)$' } | Select-Object -ExpandProperty Name
