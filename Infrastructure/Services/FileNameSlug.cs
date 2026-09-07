using System.Text;

namespace Infrastructure.Services
{
    public static class FileNameSlug
    {
        private const int MaxLength = 60;
        private const string Fallback = "nostalgia-video";

        public static string Create(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return Fallback;
            }
            var builder = new StringBuilder();
            var lastWasDash = false;
            foreach (var c in title.Trim())
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    builder.Append(c);
                    lastWasDash = false;
                }
                else if (c is ' ' or '-' or '_')
                {
                    if (!lastWasDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        lastWasDash = true;
                    }
                }

                if (builder.Length >= MaxLength)
                {
                    break;
                }
            }
            var slug = builder.ToString().Trim('-');
            return slug.Length == 0 ? Fallback : slug;
        }
    }
}
