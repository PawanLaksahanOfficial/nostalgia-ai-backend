using System.Security.Cryptography;

namespace Infrastructure.Services
{
    public static class ShareTokenGenerator
    {
        public static string Create(int byteCount = 16)
        {
            var bytes = RandomNumberGenerator.GetBytes(byteCount);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
