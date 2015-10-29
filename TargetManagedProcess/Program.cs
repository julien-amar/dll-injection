using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TargetManagedProcess
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Target Managed Process ID : {0}", Process.GetCurrentProcess().Id);

            for (int i = 0; true; ++i)
            {
                Console.WriteLine("[TARGET] Hook triggering.");

                Console.ForegroundColor = i % 2 == 0 ? ConsoleColor.Cyan : ConsoleColor.Green;

                Console.WriteLine("[TARGET] Hook triggered.");

                Thread.Sleep(1000);
            }
        }
    }
}
