using System.Net;

namespace NetworkMonitor.Shared.Utils;

public static class CidrHelper
{
    public static List<string> EnumerateHosts(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2)
            return new List<string>();

        var baseIp = IPAddress.Parse(parts[0]);
        int prefix = int.Parse(parts[1]);

        uint ip = BitConverter.ToUInt32(baseIp.GetAddressBytes().Reverse().ToArray(), 0);
        uint mask = uint.MaxValue << (32 - prefix);
        uint network = ip & mask;
        uint broadcast = network + ~mask;

        var result = new List<string>();

        for (uint addr = network + 1; addr < broadcast; addr++)
        {
            var bytes = BitConverter.GetBytes(addr).Reverse().ToArray();
            result.Add(new IPAddress(bytes).ToString());
        }

        return result;
    }
}