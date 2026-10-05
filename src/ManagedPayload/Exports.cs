using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ManagedPayload;

public static class Exports
{
    const string LogFile = "reloaded_inject_demo.log";

    // Invoked by the injector after the DLL is loaded, via
    // Reloaded's CallFunction(dll, "Run", args, marshal: true): the injector
    // marshals a struct into the target and passes a pointer to it. Our struct's
    // first field is an inline ANSI string, so `arg` points straight at the text.
    //
    // The entire body is guarded: an exception must never escape an
    // [UnmanagedCallersOnly] method, or it tears down the host process.
    [UnmanagedCallersOnly(EntryPoint = "Run")]
    public static int Run(IntPtr arg)
    {
        try
        {
            int pid = Environment.ProcessId;
            string message = arg != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(arg) ?? "(null)"
                : "(no message)";

            string line = $"[MANAGED] payload Run() in PID {pid}: {message}\n";

            try { Console.Write(line); } catch { /* host may have no console */ }

            string path = Path.Combine(Path.GetTempPath(), LogFile);
            File.AppendAllText(path, line);

            return 0; // success
        }
        catch
        {
            return 1; // surfaced to the injector as the call's return code
        }
    }
}
