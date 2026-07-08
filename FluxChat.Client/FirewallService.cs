using System.Diagnostics;
using System.Security.Principal;

namespace FluxChat.Client;

internal static class FirewallService
{
    public const string SetupArgument = "--setup-firewall";

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void RunElevatedSetup()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new InvalidOperationException("Could not find the current executable path.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = SetupArgument,
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    public static async Task ApplyRulesAsync()
    {
        DeleteRule("FluxChat LAN Discovery UDP 42731");
        DeleteRule("FluxChat LAN Messages TCP 42732");
        DeleteRule("FluxChat LAN Messages UDP 42732");

        await AddRuleAsync("FluxChat LAN Messages TCP 42732", "TCP", "42732");
        await AddRuleAsync("FluxChat LAN Messages UDP 42732", "UDP", "42732");
    }

    private static void DeleteRule(string name)
    {
        using var process = StartNetsh($"advfirewall firewall delete rule name=\"{name}\"");
        process.WaitForExit();
    }

    private static async Task AddRuleAsync(string name, string protocol, string port)
    {
        using var process = StartNetsh(
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} localport={port} profile=any");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh failed while adding {name}.");
        }
    }

    private static Process StartNetsh(string arguments)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start netsh.");
    }
}
