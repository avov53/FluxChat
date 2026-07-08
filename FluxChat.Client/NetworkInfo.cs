using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FluxChat.Client;

internal static class NetworkInfo
{
    public static string GetPrimaryLocalIPv4()
    {
        var address = GetLanIPv4Addresses()
            .Select(x => x.Address.ToString())
            .FirstOrDefault();

        return address ?? "No IPv4 address found";
    }

    public static IReadOnlyList<LanIPv4Address> GetLanIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up)
            .Where(IsUsableChatInterface)
            .SelectMany(networkInterface =>
            {
                var properties = networkInterface.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork);

                return properties.UnicastAddresses
                    .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork && x.IPv4Mask is not null)
                    .Where(x => !IPAddress.IsLoopback(x.Address) && !IsAutomaticPrivateAddress(x.Address))
                    .Select(x => new LanIPv4Address(
                        x.Address,
                        x.IPv4Mask!,
                        GetBroadcastAddress(x.Address, x.IPv4Mask!),
                        hasGateway,
                        networkInterface.NetworkInterfaceType));
            })
            .OrderByDescending(x => x.HasGateway)
            .ThenBy(x => x.InterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .ToList();
    }

    public static IPAddress? GetLocalAddressFor(IPAddress remoteAddress)
    {
        var addresses = GetLanIPv4Addresses();
        return addresses.FirstOrDefault(x => IsSameSubnet(x.Address, remoteAddress, x.SubnetMask))?.Address
            ?? addresses.FirstOrDefault()?.Address;
    }

    private static bool IsUsableChatInterface(NetworkInterface networkInterface)
    {
        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
        {
            return !LooksBlockedVirtualAdapter(networkInterface);
        }

        return LooksVpnAdapter(networkInterface);
    }

    private static bool LooksVpnAdapter(NetworkInterface networkInterface)
    {
        var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();
        string[] vpnMarkers =
        [
            "wireguard",
            "tailscale",
            "zerotier",
            "vpn",
            "tap",
            "tun",
            "wintun"
        ];

        return vpnMarkers.Any(text.Contains);
    }

    private static bool LooksBlockedVirtualAdapter(NetworkInterface networkInterface)
    {
        var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();
        string[] virtualMarkers =
        [
            "virtual",
            "vmware",
            "hyper-v",
            "virtualbox",
            "npcap",
            "loopback"
        ];

        return virtualMarkers.Any(text.Contains);
    }

    private static bool IsAutomaticPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        var broadcastBytes = new byte[addressBytes.Length];

        for (var i = 0; i < broadcastBytes.Length; i++)
        {
            broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
        }

        return new IPAddress(broadcastBytes);
    }

    private static bool IsSameSubnet(IPAddress left, IPAddress right, IPAddress subnetMask)
    {
        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();

        for (var i = 0; i < leftBytes.Length; i++)
        {
            if ((leftBytes[i] & maskBytes[i]) != (rightBytes[i] & maskBytes[i]))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record LanIPv4Address(
    IPAddress Address,
    IPAddress SubnetMask,
    IPAddress BroadcastAddress,
    bool HasGateway,
    NetworkInterfaceType InterfaceType);
