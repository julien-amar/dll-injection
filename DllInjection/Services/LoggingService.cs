using System;
using System.Diagnostics;
using System.Security;

namespace DllInjection.Services
{
    public class LoggingService
    {
        private const string SOURCE = "Dll Infector";

        public LoggingService()
        {
            try
            {
                if (!EventLog.SourceExists(SOURCE))
                    EventLog.CreateEventSource(SOURCE, "Application");
            }
            catch (SecurityException)
            {
            }
        }

        public void Log(EventLogEntryType type, string message)
        {
            try
            {
                EventLog appLog = new EventLog()
                {
                    Source = SOURCE,
                };

                appLog.WriteEntry(message, type);
            }
            catch
            {
            }
        }
    }
}
