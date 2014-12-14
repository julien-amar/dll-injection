using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DllInfection
{
    public class MessengerService : MarshalByRefObject
    {
        public void ReportError(Int32 InClientPID, Exception e)
        {
            Console.WriteLine("[IPC] A client process ({0}) has reported an error : {1}", InClientPID, e.ToString());
        }

        public bool Ping(Int32 InClientPID)
        {
            Console.WriteLine("[IPC] Ping from target process ({0}) received.", InClientPID);

            return true;
        }

        public void OnHook()
        {
            Console.WriteLine("[IPC] Hook had been notified to hosting process.");
        }
    }
}
