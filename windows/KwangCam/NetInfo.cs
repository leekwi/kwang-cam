using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace KwangCam;

internal static class NetInfo
{
    private static readonly string[] VirtualWords =
    {
        "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "loopback",
        "pseudo", "tap-", "tunnel", "wsl", "docker", "bluetooth", "zerotier", "tailscale"
    };

    /// <summary>
    /// 폰이 접속할 수 있는 이 PC의 내부망 IPv4 주소들. 와이파이를 먼저, 가상 어댑터는 제외.
    /// </summary>
    public static List<string> GetLanAddresses()
    {
        var result = new List<(string ip, int rank)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Wireless80211
                or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet)) continue;

            var desc = (nic.Description + " " + nic.Name).ToLowerInvariant();
            if (VirtualWords.Any(desc.Contains)) continue;

            var props = nic.GetIPProperties();
            // 기본 게이트웨이가 없는 어댑터는 보통 실제 공유기에 연결된 게 아님
            bool hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (!IsPrivate(ua.Address)) continue;
                int rank = (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
                           + (hasGateway ? 0 : 10);
                result.Add((ua.Address.ToString(), rank));
            }
        }
        return result.OrderBy(r => r.rank).Select(r => r.ip).Distinct().ToList();
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168);
    }
}
