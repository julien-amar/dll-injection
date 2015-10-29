using DllInjection.Services;
using EasyHook;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DllInjection
{
    public class Injection : IEntryPoint
    {
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern bool SetConsoleTextAttribute(IntPtr hConsoleOutput, short attributes);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        public delegate bool SetConsoleTextAttributeDelegate(IntPtr hConsoleOutput, short attributes);

        public MessengerService Messenger { get; private set; }
        public LoggingService Logger { get; private set; }

        public Injection(RemoteHooking.IContext InContext, string ipcChannelName)
        {
            Messenger = RemoteHooking.IpcConnectClient<MessengerService>(ipcChannelName);

            Logger = new LoggingService();

            Logger.Log(EventLogEntryType.Information, "[INFECTION] Infection had been initialized");
        }

        public void Run(RemoteHooking.IContext InContext, string ipcChannelName)
        {
            try
            {
                Logger.Log(EventLogEntryType.Information, "[INFECTION] Hooking SetConsoleTextAttribute function (kernel32.dll)");

                var myHook = LocalHook.Create(
                    LocalHook.GetProcAddress("kernel32.dll", "SetConsoleTextAttribute"),
                    new SetConsoleTextAttributeDelegate(Hooked),
                    this);

                Logger.Log(EventLogEntryType.Information, "[INFECTION] Setting an exclusive ACL");

                myHook.ThreadACL.SetExclusiveACL(new int[1] { 0 });
            }
            catch (Exception e)
            {
                Logger.Log(EventLogEntryType.Error, "[INFECTION] Hooking failed: " + e.ToString());
            }

            try
            {
                RemoteHooking.WakeUpProcess();

                Logger.Log(EventLogEntryType.Information, "[INFECTION] Running");

                while (Messenger.Ping(RemoteHooking.GetCurrentProcessId()))
                {
                    Thread.Sleep(1000);
                }

                Logger.Log(EventLogEntryType.Information, "[INFECTION] Stoppped");
            }
            catch (Exception e)
            {
                Logger.Log(EventLogEntryType.Error, "[INFECTION] An error occured during execution: " + e.ToString());
            }
        }

        public static bool Hooked(IntPtr hConsoleOutput, short attributes)
        {
            Injection This = (Injection)HookRuntimeInfo.Callback;

            try
            {
                This.Logger.Log(EventLogEntryType.Information, "[INFECTION] Hook had been triggered");

                This.Messenger.OnHook();

                This.Logger.Log(EventLogEntryType.Information, "[INFECTION] Calling native implementation of the function");

                return SetConsoleTextAttribute(hConsoleOutput, attributes);
            }
            catch (Exception e)
            {
                if (This != null)
                    This.Logger.Log(EventLogEntryType.Error, "[INFECTION] An error occured during execution: " + e.ToString());

                return false;
            }
        }
    }
}
