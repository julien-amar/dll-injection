using System;
using System.Diagnostics;

namespace DllInjection.Services
{
    public class MessengerService : MarshalByRefObject
    {
        public LoggingService Logger { get; private set; }

        public MessengerService()
        {
            Logger = new LoggingService();
        }

        public void ReportError(Int32 InClientPID, Exception e)
        {
            Logger.Log(EventLogEntryType.Information, String.Format("[IPC] A client process ({0}) has reported an error : {1}", InClientPID, e.ToString()));
        }

        public bool Ping(Int32 InClientPID)
        {
            Logger.Log(EventLogEntryType.Information, String.Format("[IPC] Ping from target process ({0}) received.", InClientPID));

            return true;
        }

        public void OnHook()
        {
            Logger.Log(EventLogEntryType.Information, "[IPC] Hook had been notified to hosting process.");
        }
    }
}
