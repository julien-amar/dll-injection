// Minimal managed (.NET) target process.
// Loops for a few seconds so a payload can be injected into it, then exits.

int pid = Environment.ProcessId;
Console.WriteLine($"[managed-target] started, PID = {pid}");

for (int i = 0; i < 20; i++)
{
    Console.WriteLine($"[managed-target] tick {i}");
    Thread.Sleep(500);
}

Console.WriteLine("[managed-target] done");
