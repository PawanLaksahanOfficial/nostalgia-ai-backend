namespace Infrastructure.Services
{
    public static class PhotoCreditBuilder
    {
        // Stored whole, e.g. "Ana Silva, Ravi Perera on Pixabay", so every video keeps crediting the site
        // its photos came from even if the configured provider changes later.
        public static string? Build(IEnumerable<string> photographers, string sourceName, int maxLength)
        {
            var names = string.Join(", ", photographers
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (names.Length == 0)
            {
                return null;
            }
            var suffix = $" on {sourceName}";
            var room = Math.Max(0, maxLength - suffix.Length);
            return (names.Length > room ? names[..room].TrimEnd(' ', ',') : names) + suffix;
        }
    }
}
