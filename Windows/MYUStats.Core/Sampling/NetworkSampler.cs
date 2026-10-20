using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using MYUStats.Native;

namespace MYUStats.Sampling;

public static class NetworkMath
{
    /// <summary>Bytes per second between two readings of the counters. A counter that went backwards
    /// (an adapter restarted) reads as no traffic rather than a huge jump.</summary>
    public static NetworkRate Rate(NetworkTotals before, NetworkTotals now, double seconds)
    {
        if (seconds <= 0) return default;
        var down = now.Received >= before.Received ? now.Received - before.Received : 0;
        var up = now.Sent >= before.Sent ? now.Sent - before.Sent : 0;
        return new NetworkRate(down / seconds, up / seconds);
    }
}

/// <summary>Throughput across hardware adapters, read from their 64-bit counters so large transfers never wrap.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class NetworkSampler
{
    private (NetworkTotals Totals, long Timestamp)? previous;

    /// <summary>Bytes counted since each adapter last started.</summary>
    public NetworkTotals Totals { get; private set; }

    public NetworkRate Sample()
    {
        if (ReadTotals() is not { } current) return default;
        Totals = current;
        var now = Stopwatch.GetTimestamp();
        var before = previous;
        previous = (current, now);
        if (before is not { } earlier || now <= earlier.Timestamp) return default;
        return NetworkMath.Rate(earlier.Totals, current, Stopwatch.GetElapsedTime(earlier.Timestamp, now).TotalSeconds);
    }

    private static NetworkTotals? ReadTotals()
    {
        if (Iphlpapi.GetIfTable2(out var table) != 0 || table == 0) return null;
        try
        {
            var count = *(uint*)table;
            var length = NativeParsers.IfTableHeader + (long)count * NativeParsers.IfRowSize;
            if (length > int.MaxValue) return null;
            return NativeParsers.NetworkTotals(new ReadOnlySpan<byte>((void*)table, (int)length));
        }
        finally
        {
            Iphlpapi.FreeMibTable(table);
        }
    }

    /// <summary>The connection with a default route, for example Wi-Fi · 192.168.1.5. Wired and wireless
    /// adapters come before others, so a VPN with its own gateway does not take the place of the link.</summary>
    public static NetworkInterfaceInfo? PrimaryInterface()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderBy(adapter => adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                    or NetworkInterfaceType.GigabitEthernet ? 0 : 1);
            foreach (var adapter in candidates)
            {
                var properties = adapter.GetIPProperties();
                var hasRoute = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));
                if (!hasRoute) continue;
                var address = properties.UnicastAddresses.FirstOrDefault(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork);
                if (address is not null) return new NetworkInterfaceInfo(adapter.Name, address.Address.ToString());
            }
        }
        catch (NetworkInformationException error)
        {
            Log.Sampling.Notice($"Network adapters unavailable: {error.Message}");
        }
        return null;
    }
}
