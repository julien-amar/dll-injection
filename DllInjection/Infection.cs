using EasyHook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DllInjection
{
    public class Infection : IEntryPoint
    {
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern bool SetConsoleTextAttribute(IntPtr hConsoleOutput, short attributes);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        public delegate bool SetConsoleTextAttributeDelegate(IntPtr hConsoleOutput, short attributes);

        public MessengerService Messenger { get; set; }

        public Infection(RemoteHooking.IContext InContext, string ipcChannelName)
        {
            Console.WriteLine("[INFECTION] Initialize");

            Messenger = RemoteHooking.IpcConnectClient<MessengerService>(ipcChannelName);

            Console.WriteLine("[INFECTION] Infection had been initialized");
        }

        public void Run(RemoteHooking.IContext InContext, string ipcChannelName)
        {
            Console.WriteLine("[INFECTION] Hooking SetConsoleTextAttribute function (kernel32.dll)");

            var myHook = LocalHook.Create(
                LocalHook.GetProcAddress("kernel32.dll", "SetConsoleTextAttribute"),
                new SetConsoleTextAttributeDelegate(Hooked),
                this);

            Console.WriteLine("[INFECTION] Setting an exclusive ACL");

            myHook.ThreadACL.SetExclusiveACL(new int[1] { 0 });

            Console.WriteLine("[INFECTION] Running");

            while (Messenger.Ping(RemoteHooking.GetCurrentProcessId()))
            {
                Thread.Sleep(1000);
            }

            Console.WriteLine("[INFECTION] Stoping");
        }

        public static bool Hooked(IntPtr hConsoleOutput, short attributes)
        {
            Console.WriteLine("[INFECTION] Hook had been triggered");

            Infection This = (Infection)HookRuntimeInfo.Callback;

            This.Messenger.OnHook();

            Console.WriteLine("[INFECTION] Calling native implementation of the function");

            return SetConsoleTextAttribute(hConsoleOutput, attributes);
        }
    }
}
