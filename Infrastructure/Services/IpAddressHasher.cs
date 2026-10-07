using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services
{
    public class IpAddressHasher
    {
        private readonly byte[] _key;

        public IpAddressHasher(IConfiguration configuration)
        {
            var secret = configuration["Abuse:IpHashSecret"] is { Length: > 0 } configured
                ? configured
                : configuration["JwtSettings:Key"] ?? string.Empty;
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("nostalgia-ai-ip:" + secret));
        }

        public string? Hash(IPAddress? address)
        {
            if (address == null)
            {
                return null;
            }
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (!IsPublic(address))
            {
                return null;
            }
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // One home or phone gets a whole /64, so rotating within it must not reset the limits.
                Array.Clear(bytes, 8, 8);
            }
            return Convert.ToHexString(HMACSHA256.HashData(_key, bytes)).ToLowerInvariant();
        }

        public static bool IsPublic(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return false;
            }
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = address.GetAddressBytes();
                return !(b[0] == 0
                    || b[0] == 10
                    || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                    || (b[0] == 169 && b[1] == 254)
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168));
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal);
            }
            return false;
        }
    }
}
