using System;

namespace DarkTunnel.Master
{
    class Program
    {
        public static void Main(string[] args)
        {
            int port = 16702;
            if (args.Length > 0)
            {
                if (!int.TryParse(args[0], out port))
                {
                    Console.WriteLine($"Unable to parse {args[0]} as a port number.");
                }
            }

            MasterServer m = new MasterServer(port);
            Console.WriteLine($"Listening on port {port}");

            Console.CancelKeyPress += (s, e) => { Quit(m); };

            Console.WriteLine("Press q or ctrl+c to quit.");
            bool hasConsole = true;
            bool running = true;
            while (running)
            {
                if (hasConsole)
                {
                    try
                    {
                        ConsoleKeyInfo cki = Console.ReadKey(false);
                        if (cki.KeyChar == 'q')
                        {
                            running = false;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        Console.WriteLine("Program does not have a console, not listening for console input.");
                        hasConsole = false;
                    }
                }
                else
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }

        private static void Quit(MasterServer m)
        {
            m.Stop();
        }
    }
}
