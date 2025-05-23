using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("HTTPDumpProxy - Usage:");
            Console.WriteLine();
            Console.WriteLine("  --host <ip>            Host to bind the proxy to (default: 127.0.0.1)");
            Console.WriteLine("  --port <port>          Port to listen on (default: 8080)");
            Console.WriteLine("  --timeout <seconds>    Proxy lifetime timeout in seconds (default: 30)");
            Console.WriteLine("  --log <filename>       Path to the file where HTTP requests will be logged (default: requests.log)");
            Console.WriteLine("  --filter <pattern>     Optional regex pattern to filter which requests/responses are logged");
            Console.WriteLine("  --help                 Show this help message and exit");
            Environment.Exit(0);
        }
        AppDomain.CurrentDomain.ProcessExit += (_, __) => Cleanup();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[!] Keyboard interrupt. Cleanup...");
            Cleanup();
            Environment.Exit(0);
        };
        // Default values
        string host = "127.0.0.1";
        int port = 8080;
        int timeout = 30;
        string logFile = "requests.log";
        string? filterPattern = ".*";
        // Parse args
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host":
                    host = args[++i];
                    break;
                case "--port":
                    port = int.Parse(args[++i]);
                    break;
                case "--timeout":
                    timeout = int.Parse(args[++i]);
                    break;
                case "--log":
                    logFile = args[++i];
                    break;
                case "--filter":
                    filterPattern = args[++i];
                    break;
            }
        }
        string asciiBanner = @"
  _    _ _______ _______ _____  _____                        _____                     
 | |  | |__   __|__   __|  __ \|  __ \                      |  __ \                    
 | |__| |  | |     | |  | |__) | |  | |_   _ _ __ ___  _ __ | |__) | __ _____  ___   _ 
 |  __  |  | |     | |  |  ___/| |  | | | | | '_ ` _ \| '_ \|  ___/ '__/ _ \ \/ / | | |
 | |  | |  | |     | |  | |    | |__| | |_| | | | | | | |_) | |   | | | (_) >  <| |_| |
 |_|  |_|  |_|     |_|  |_|    |_____/ \__,_|_| |_| |_| .__/|_|   |_|  \___/_/\_\\__, |
                                                      | |                         __/ |
                                                      |_|                        |___/ 
";
        /* MAIN */
        Console.WriteLine(asciiBanner);
        Logger.Initialize(logFile, filterPattern);
        // CA config
        CertificateAuthority.SetupRootCertificate();
        // Proxy config
        SystemProxyConfigurator.EnableSystemProxy(host, port);
        // Proxy listener
        var server = new ProxyServer(host, port);
        Console.WriteLine("[*] Starting Proxy...");
        var serverTask = server.StartAsync();
        await Task.Delay(timeout*1000);
        Console.WriteLine("[*] Reached timeout, stopping Proxy...");
        await server.StopAsync();
        // Cleanup
        Cleanup();
    }

    static void Cleanup()
    {
        SystemProxyConfigurator.DisableSystemProxy();
        CertificateAuthority.CleanupCertificates();
        Console.WriteLine("[*] Cleanup succeed.");
    }
}
