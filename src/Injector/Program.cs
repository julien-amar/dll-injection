using System.Diagnostics;
using System.Runtime.InteropServices;
using Reloaded.Injector;

// Generic DLL injector built on Reloaded.Injector.
//
// Usage:
//   Injector <targetExe>  <payloadDll> [--export NAME] [--msg "text"]
//   Injector --pid <pid>  <payloadDll> [--export NAME] [--msg "text"]
//
// Omit --export for native payloads   -> they run from DllMain.
// Pass --export NAME for managed payloads -> that export is invoked after load.
//
// --msg is the data passed into the payload. For a launched target it is set as
// the inherited env var RELOADED_PAYLOAD_MSG (read by DllMain payloads) and, when
// an export is given, also marshalled into the target and passed to it.

internal static class Program
{
    const string Usage = "usage: Injector <targetExe | --pid N> <payloadDll> [--export NAME] [--msg \"text\"]";

    // A DllMain payload cannot be handed call arguments, so the message is left for
    // it in a per-PID sidecar file. Keyed by PID so it works whether we launched the
    // target or attached to a running one, and so concurrent runs never collide.
    static string MessagePath(int pid) =>
        Path.Combine(Path.GetTempPath(), $"reloaded_payload_msg_{pid}.txt");

    static int Main(string[] args)
    {
        try
        {
            return Inject(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[injector] error: {ex.Message}");
            return 1;
        }
    }

    static int Inject(string[] args)
    {
        // --- parse arguments ---
        int i = 0;
        bool attach = false;
        int pid = 0;
        string? targetExe = null;

        if (args.Length >= 1 && args[0] == "--pid")
        {
            if (args.Length < 2 || !int.TryParse(args[1], out pid))
            {
                Console.Error.WriteLine("error: --pid requires a numeric process id");
                return 1;
            }
            attach = true;
            i = 2;
        }
        else if (args.Length >= 1)
        {
            targetExe = args[0];
            i = 1;
        }

        if (i >= args.Length)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        string payload = Path.GetFullPath(args[i++]);
        string? export = null;
        string message = "hello from the injector";

        for (; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--export" when i + 1 < args.Length: export = args[++i]; break;
                case "--msg" when i + 1 < args.Length: message = args[++i]; break;
                default:
                    Console.Error.WriteLine($"error: unexpected argument '{args[i]}'");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        if (!File.Exists(payload))
        {
            Console.Error.WriteLine($"error: payload not found: {payload}");
            return 1;
        }

        Process target;
        bool launched = !attach;

        if (attach)
        {
            target = Process.GetProcessById(pid);
            Console.WriteLine($"[injector] attaching to existing PID {target.Id}");
        }
        else
        {
            // Textbook control flow: create the target SUSPENDED so we fully own
            // it before it runs, set its (inherited) environment, then resume.
            //
            // We inject AFTER resuming, not while suspended: a suspended process
            // has only ntdll mapped - the loader (which maps kernel32) has not run
            // yet, and Reloaded injects via kernel32!LoadLibrary. So we resume and
            // inject the moment the loader has mapped kernel32. No fixed sleep, no
            // race against a hard-coded delay.
            string exe = Path.GetFullPath(targetExe!);
            IntPtr mainThread;
            (target, mainThread) = StartSuspended(exe);
            Console.WriteLine($"[injector] created {Path.GetFileName(exe)} suspended, PID {target.Id}");

            ResumeThread(mainThread);
            CloseHandle(mainThread);
            WaitUntilInjectable(target);
        }

        using (target)
        {
            if (!ArchMatches(target))
            {
                Console.Error.WriteLine("[injector] architecture mismatch: injector and target must both be x64 (or both x86)");
                if (launched) target.Kill();
                return 3;
            }

            // Leave the message where a DllMain payload can pick it up (see MessagePath).
            File.WriteAllText(MessagePath(target.Id), message);

            using var injector = new Injector(target);

            long handle = injector.Inject(payload);
            if (handle == 0)
            {
                Console.Error.WriteLine($"[injector] FAILED to inject {Path.GetFileName(payload)}");
                if (launched) target.Kill();
                return 2;
            }
            Console.WriteLine($"[injector] injected {Path.GetFileName(payload)} @ 0x{handle:X}");

            int result = 0;
            if (export is not null)
            {
                // Guard: calling a non-existent export would run a remote thread at
                // a bogus address and crash the target (0xC0000005).
                if (injector.GetFunctionAddress(payload, export) == 0)
                {
                    Console.Error.WriteLine($"[injector] export '{export}' not found in {Path.GetFileName(payload)}");
                    if (launched) target.Kill();
                    return 4;
                }

                // Marshal a PayloadArgs struct into the target and pass its pointer.
                var payloadArgs = new PayloadArgs { Message = message };
                result = injector.CallFunction(payload, export, payloadArgs, marshalParameter: true);
                Console.WriteLine($"[injector] called {export}(\"{message}\") -> {result}");
            }

            if (launched)
            {
                target.WaitForExit();
                Console.WriteLine("[injector] target exited");
            }
            else
            {
                Console.WriteLine("[injector] done (target left running)");
            }

            return export is not null ? result : 0;
        }
    }

    // Data handed to a managed payload export. First field is an inline ANSI
    // string, so the pointer passed to the export points straight at the text.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct PayloadArgs
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Message;
    }

    static (Process process, IntPtr mainThread) StartSuspended(string exe)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        bool ok = CreateProcess(exe, null, IntPtr.Zero, IntPtr.Zero, false,
            CREATE_SUSPENDED, IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out PROCESS_INFORMATION pi);

        if (!ok)
            throw new InvalidOperationException($"CreateProcess failed (win32 error {Marshal.GetLastWin32Error()})");

        CloseHandle(pi.hProcess);
        return (Process.GetProcessById(pi.dwProcessId), pi.hThread);
    }

    // Wait until the loader has mapped kernel32 (so LoadLibrary injection is safe).
    // Polls a readiness condition rather than sleeping a fixed amount of time.
    static void WaitUntilInjectable(Process target, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (target.HasExited)
                throw new InvalidOperationException("target exited before it could be injected");
            try
            {
                target.Refresh();
                foreach (ProcessModule m in target.Modules)
                    if (string.Equals(m.ModuleName, "kernel32.dll", StringComparison.OrdinalIgnoreCase))
                        return;
            }
            catch
            {
                // Loader not finished populating the module list yet; keep polling.
            }
            Thread.Sleep(15);
        }
        throw new TimeoutException("target did not finish initialising in time");
    }

    static bool ArchMatches(Process target)
    {
        if (!Environment.Is64BitOperatingSystem) return true; // 32-bit OS: everything is x86
        if (!IsWow64Process(target.Handle, out bool targetIsWow64)) return true; // can't tell; don't block
        bool targetIs64 = !targetIsWow64;
        return targetIs64 == Environment.Is64BitProcess;
    }

    const uint CREATE_SUSPENDED = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateProcess(string? applicationName, string? commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll")]
    static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);
}
