using System.Security.Cryptography;

namespace Infrastructure.Services
{
    public static class ShareTokenGenerator
    {
        public static string Create()
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
