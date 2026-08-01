using System.Diagnostics;

namespace FluxChat.Client;

internal static class GameActivityDetector
{
    private static readonly IReadOnlyDictionary<string, GameActivity> KnownGames =
        new Dictionary<string, GameActivity>(StringComparer.OrdinalIgnoreCase)
        {
            ["RobloxPlayerBeta"] = new("Roblox", "\U0001F3AE"),
            ["RobloxPlayer"] = new("Roblox", "\U0001F3AE"),
            ["Minecraft.Windows"] = new("Minecraft", "\u26CF\uFE0F"),
            ["MinecraftLauncher"] = new("Minecraft", "\u26CF\uFE0F"),
            ["FortniteClient-Win64-Shipping"] = new("Fortnite", "\U0001F3DD\uFE0F"),
            ["VALORANT-Win64-Shipping"] = new("VALORANT", "\U0001F3AF"),
            ["cs2"] = new("Counter-Strike 2", "\U0001F3AF"),
            ["dota2"] = new("Dota 2", "\u2694\uFE0F"),
            ["League of Legends"] = new("League of Legends", "\u2694\uFE0F"),
            ["Overwatch"] = new("Overwatch 2", "\U0001F3AF"),
            ["r5apex"] = new("Apex Legends", "\U0001F3AF"),
            ["GTA5"] = new("Grand Theft Auto V", "\U0001F697"),
            ["PlayGTAV"] = new("Grand Theft Auto V", "\U0001F697"),
            ["FiveM"] = new("FiveM", "\U0001F697"),
            ["RDR2"] = new("Red Dead Redemption 2", "\U0001F920"),
            ["RustClient"] = new("Rust", "\U0001F6E0\uFE0F"),
            ["TslGame"] = new("PUBG", "\U0001F3AF"),
            ["RocketLeague"] = new("Rocket League", "\u26BD"),
            ["Among Us"] = new("Among Us", "\U0001F680"),
            ["AmongUs"] = new("Among Us", "\U0001F680"),
            ["Terraria"] = new("Terraria", "\U0001F332"),
            ["Stardew Valley"] = new("Stardew Valley", "\U0001F331"),
            ["StardewValley"] = new("Stardew Valley", "\U0001F331"),
            ["GenshinImpact"] = new("Genshin Impact", "\u2728"),
            ["YuanShen"] = new("Genshin Impact", "\u2728"),
            ["DeadByDaylight-Win64-Shipping"] = new("Dead by Daylight", "\U0001F526"),
            ["osu!"] = new("osu!", "\U0001F3B5"),
            ["WorldOfTanks"] = new("World of Tanks", "\U0001F6E1\uFE0F"),
            ["Wow"] = new("World of Warcraft", "\u2694\uFE0F"),
            ["WowClassic"] = new("World of Warcraft Classic", "\u2694\uFE0F"),
            ["WowClassicT"] = new("World of Warcraft Classic", "\u2694\uFE0F"),
            ["World of Warcraft"] = new("World of Warcraft", "\u2694\uFE0F"),
            ["Warcraft III"] = new("Warcraft III", "\u2694\uFE0F"),
            ["Warcraft III Launcher"] = new("Warcraft III", "\u2694\uFE0F"),
            ["Warcraft"] = new("Warcraft", "\u2694\uFE0F"),
            ["aces"] = new("War Thunder", "\u2708\uFE0F")
        };

    public static GameActivity? Detect()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        GameActivity? newestGame = null;
        DateTime newestStartTime = DateTime.MinValue;
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (KnownGames.TryGetValue(process.ProcessName, out var game))
                    {
                        var startTime = GetStartTime(process);
                        if (newestGame is null || startTime >= newestStartTime)
                        {
                            newestGame = game;
                            newestStartTime = startTime;
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // A process can exit while the snapshot is being inspected.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return newestGame;
    }

    private static DateTime GetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return DateTime.MinValue;
        }
    }
}

internal sealed record GameActivity(string Name, string Icon);
