dll-injection
=============

Minimal, modern DLL-injection examples built on
[Reloaded.Injector](https://github.com/Reloaded-Project/Reloaded.Injector).
They demonstrate every combination of injecting a **native** or **managed (C#)**
DLL into a **native** or **managed (C#)** process:

| # | Payload (injected DLL) | Target process | Mechanism |
|---|------------------------|----------------|-----------|
| 1 | native (Rust)   | native (Rust)  | `LoadLibrary` → `DllMain` |
| 2 | managed (C#)    | native (Rust)  | `LoadLibrary` → call export |
| 3 | native (Rust)   | managed (C#)   | `LoadLibrary` → `DllMain` |
| 4 | managed (C#)    | managed (C#)   | `LoadLibrary` → call export |

> **For authorized, educational use only.** Inject only into processes you own or
> are explicitly permitted to test. Injection is routinely flagged by security
> software.

How it works
------------

Injection itself is identical whether the target is native or managed — to the OS
a .NET process is just a normal Win32 process, so `Reloaded.Injector` injects into
both the same way. The differences are in the **payload** and in **how data is
passed in**:

- **Native payload** (`native_payload.dll`, Rust `cdylib`): exposes `DllMain`, so the
  loader runs it automatically on `DLL_PROCESS_ATTACH`. Because `DllMain` runs under
  the **loader lock** (where almost nothing is safe to do), it does the minimum — spawns
  a worker thread and returns — and the real work runs on that thread. Since a `DllMain`
  payload cannot be handed call arguments, the injector leaves the `--msg` data in a
  per-PID file (`%TEMP%\reloaded_payload_msg_<pid>.txt`) that the payload reads and
  deletes — this works whether the target was launched or attached to with `--pid`.
- **Managed payload** (`ManagedPayload.dll`, C#): compiled with **.NET NativeAOT**
  (`<NativeLib>Shared</NativeLib>`) into a *self-contained native DLL* that exports a
  plain C function `Run`. Being AOT-compiled it carries **no CLR dependency** and never
  conflicts with a managed target's own .NET runtime. The injector calls `Run` after
  loading the DLL and passes data by marshalling a small struct into the target.

Each payload appends one line to `%TEMP%\reloaded_inject_demo.log` (and prints to the
target's console if it has one) so you can confirm it executed and see the message.

### Launch sequence

For a launched target the injector creates the process **suspended**, sets its
environment, then **resumes** it and injects as soon as the loader has mapped
`kernel32` — there is **no fixed sleep**, so it is not racing a hard-coded delay.

> Note: injection happens just *after* resume rather than while suspended. Reloaded
> injects via `kernel32!LoadLibrary`, and `kernel32` is not mapped into a suspended
> process (only `ntdll` is) until the loader runs on the first thread. True
> inject-before-first-instruction would require an `ntdll!LdrLoadDll`-based injector,
> which is out of scope for this minimal sample.

Layout
------

```
src/Injector/        C# console app  — the injector (Reloaded.Injector)
src/ManagedTarget/   C# console exe  — a managed target process
src/ManagedPayload/  C# NativeAOT    — the managed payload (native DLL exporting Run)
native/native_target/   Rust binary  — a native target process
native/native_payload/  Rust cdylib  — the native payload (DllMain)
build.ps1            Builds everything into .\dist
test.ps1             Runs all four scenarios and verifies them
ReloadedExamples.slnx  Solution for the three C# projects
```

Prerequisites
-------------

- 64-bit Windows.
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download) (tested with 8 and 10).
- [Rust](https://rustup.rs/) with the `x86_64-pc-windows-msvc` toolchain.
- Visual Studio **Desktop development with C++** workload (MSVC + Windows SDK).
  This supplies the linker used by both Rust-MSVC and .NET NativeAOT.

No administrator rights are needed to inject into a process you launched yourself
(same user, same integrity level). Injecting into other or elevated processes does
require elevation.

Build
-----

```powershell
.\build.ps1
```

This cleans `dist`, builds the Rust crates (pinned to `x86_64-pc-windows-msvc`), the
C# injector and managed target, publishes the NativeAOT managed payload, and copies
every artifact into `.\dist`. The script enters the Visual Studio developer
environment automatically (native linking needs it). When it finishes, `dist\`
contains:

```
Injector.exe          native_target.exe     native_payload.dll
ManagedTarget.exe     ManagedPayload.dll    (+ Injector/Reloaded dependencies)
```

Run the examples
----------------

The injector is generic:

```
Injector <targetExe | --pid N> <payloadDll> [--export NAME] [--msg "text"]
```

Omit `--export` for native payloads (they run from `DllMain`); pass `--export Run`
for the managed payload. `--msg` is the data passed into the payload. Run the four
scenarios from `dist`:

```powershell
cd dist

# 1. native payload  -> native process
.\Injector.exe native_target.exe native_payload.dll --msg "hi from injector"

# 2. managed payload -> native process
.\Injector.exe native_target.exe ManagedPayload.dll --export Run --msg "hi from injector"

# 3. native payload  -> managed process
.\Injector.exe ManagedTarget.exe native_payload.dll --msg "hi from injector"

# 4. managed payload -> managed process
.\Injector.exe ManagedTarget.exe ManagedPayload.dll --export Run --msg "hi from injector"
```

Each run creates the target, injects the payload, and exits when the target finishes
(~10 s). Confirm the payloads ran and received the message:

```powershell
Get-Content $env:TEMP\reloaded_inject_demo.log
# [NATIVE]  payload loaded in PID ....: hi from injector
# [MANAGED] payload Run() in PID ....: hi from injector
```

To inject into an already-running process instead of launching one, use
`--pid <id>` in place of the target path.

Test
----

```powershell
.\test.ps1
```

Runs all four payload×target combinations in **both** injection modes — launch
(the injector creates the target suspended) and `--pid` attach (the target is
already running) — eight scenarios in all, asserting each payload executed with the
expected message (building first if needed). This is also what CI runs — see
[.github/workflows/ci.yml](.github/workflows/ci.yml).

Notes
-----

- **Bitness must match.** Injector, payload, and target are all x64 here; the Rust
  build is pinned to `x86_64-pc-windows-msvc` and the injector refuses to inject on a
  bitness mismatch.
- **Build individually** instead of `build.ps1` if you prefer:
  `cargo build --release --target x86_64-pc-windows-msvc` in `native\`, `dotnet build`
  the two C# apps, and `dotnet publish src\ManagedPayload -c Release` for the NativeAOT
  payload — the last two must run from a *Developer PowerShell / Command Prompt for VS*
  so the linker is on `PATH`.
- **Antivirus** may quarantine the payloads or block injection; add an exclusion for
  the repo folder if needed.
