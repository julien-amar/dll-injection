using DllInjection.Services;
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

            var processId = CreateProcessAndHook(ChannelName);

            Process.GetProcessById(processId)
                .WaitForExit();
        }

        private static string CreateIpcServer()
        {
            string ChannelName = null;

            RemoteHooking.IpcCreateServer<MessengerService>(ref ChannelName, WellKnownObjectMode.Singleton);

            return ChannelName;
        }

        private static int CreateProcessAndHook(string channelName)
        {
            int processId;

            RemoteHooking.CreateAndInject(
                Environment.CurrentDirectory + @"\TargetUnManagedProcess.exe",
                "",
                0,
                InjectionOptions.DoNotRequireStrongName,
                Environment.CurrentDirectory + @"\DllInjection.dll", // 32 bits
                Environment.CurrentDirectory + @"\DllInjection.dll", // 64 bits
                out processId,
                channelName);

            return processId;
        }
    }
}
