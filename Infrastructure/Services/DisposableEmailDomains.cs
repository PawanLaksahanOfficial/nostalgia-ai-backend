using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class DisposableEmailDomains
    {
        private readonly HashSet<string> _domains;

        public DisposableEmailDomains(IEnumerable<string> domains)
        {
            _domains = new HashSet<string>(
                domains
                    .Select(domain => domain.Trim().ToLowerInvariant())
                    .Where(domain => domain.Length > 0 && !domain.StartsWith('#')),
                StringComparer.Ordinal);
        }

        public static DisposableEmailDomains FromFile(string path, ILogger logger)
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("Disposable email list '{Path}' not found; throwaway addresses will not be blocked.", path);
                return new DisposableEmailDomains(Array.Empty<string>());
            }
            return new DisposableEmailDomains(File.ReadLines(path));
        }

        public int Count => _domains.Count;

        public bool IsDisposable(string email)
        {
            var at = email.LastIndexOf('@');
            if (at < 0 || at == email.Length - 1)
            {
                return false;
            }
            var domain = email[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
            while (domain.Length > 0)
            {
                if (_domains.Contains(domain))
                {
                    return true;
                }
                var dot = domain.IndexOf('.');
                if (dot < 0)
                {
                    return false;
                }
                domain = domain[(dot + 1)..];
            }
            return false;
        }
    }
}
