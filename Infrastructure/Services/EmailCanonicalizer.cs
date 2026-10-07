namespace Infrastructure.Services
{
    public static class EmailCanonicalizer
    {
        public static string Canonicalize(string email)
        {
            var normalized = email.Trim().ToLowerInvariant();
            var at = normalized.IndexOf('@');
            if (at <= 0 || at == normalized.Length - 1)
            {
                return normalized;
            }
            var local = normalized[..at];
            var domain = normalized[(at + 1)..];
            var plus = local.IndexOf('+');
            if (plus >= 0)
            {
                local = local[..plus];
            }
            if (domain is "gmail.com" or "googlemail.com")
            {
                local = local.Replace(".", string.Empty);
                domain = "gmail.com";
            }
            return $"{local}@{domain}";
        }
    }
}
