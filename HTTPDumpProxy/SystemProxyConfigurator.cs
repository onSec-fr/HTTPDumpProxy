using Microsoft.Win32;

public static class SystemProxyConfigurator
{
    public static void EnableSystemProxy(string ip, int port)
    {
        string proxy = $"{ip}:{port}";
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable", 1);
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer", proxy);

        Console.WriteLine($"[*] System proxy enabled : {proxy}");
    }
    public static void DisableSystemProxy()
    {
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable", 0);
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer", "");

        Console.WriteLine("[*] System proxy disabled.");
    }

}
