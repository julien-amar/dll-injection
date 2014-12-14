using DllInfection;
using EasyHook;
using System;
using System.Diagnostics;
using System.Runtime.Remoting;

namespace DllInfector
{
    class Program
    {
        static void Main(string[] args)
        {
            string ChannelName = CreateIpcServer();

            var pi = StartProcess();

            Hook(ChannelName, pi);

            Process.GetProcessById((int)pi.dwProcessId)
                .WaitForExit();
        }

        private static string CreateIpcServer()
        {
            string ChannelName = null;

            RemoteHooking.IpcCreateServer<MessengerService>(ref ChannelName, WellKnownObjectMode.Singleton);

            return ChannelName;
        }

        private static W32Native.PROCESS_INFORMATION StartProcess()
        {
            var startInfo = new W32Native.STARTUPINFO();
            var processInfo = new W32Native.PROCESS_INFORMATION();

            W32Native.CreateProcess(
                Environment.CurrentDirectory + @"\TargetUnManagedProcess.exe",
                //@"C:\Users\JULIEN\Documents\Visual Studio 2013\Projects\HookRemoteProcess\Debug\HookRemoteProcess.exe",
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                0x00000004, // CREATE_SUSPENDED
                IntPtr.Zero,
                null,
                ref startInfo,
                out processInfo);

            W32Native.ResumeThread(processInfo.hThread);

            return processInfo;
        }
        
        private static void Hook(string ChannelName, W32Native.PROCESS_INFORMATION pi)
        {
            RemoteHooking.Inject(
                (int)pi.dwProcessId,
                Environment.CurrentDirectory + @"\DllInfection.dll", // 32 bits
                Environment.CurrentDirectory + @"\DllInfection.dll", // 64 bits
                ChannelName);
        }
    }
}
