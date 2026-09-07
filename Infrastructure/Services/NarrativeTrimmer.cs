using System.Text;
using System.Text.RegularExpressions;

namespace Infrastructure.Services
{
    public static class NarrativeTrimmer
    {
        private const double WordsPerSecond = 2.5;
        private const double TrailingSilenceSeconds = 2.0;
        private static readonly Regex SentenceSplitter = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

        public static int WordBudgetForSeconds(double seconds)
        {
            var speakable = Math.Max(0, seconds - TrailingSilenceSeconds);
            return Math.Max(10, (int)(speakable * WordsPerSecond));
        }

        public static string Trim(string? text, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(text) || maxWords <= 0)
            {
                return string.Empty;
            }
            var normalized = WhitespaceRun.Replace(text.Trim(), " ");
            if (CountWords(normalized) <= maxWords)
            {
                return normalized;
            }
            var builder = new StringBuilder();
            var used = 0;
            foreach (var sentence in SentenceSplitter.Split(normalized))
            {
                var words = CountWords(sentence);
                if (words == 0)
                {
                    continue;
                }
                if (used + words > maxWords)
                {
                    break;
                }
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(sentence);
                used += words;
            }
            if (builder.Length > 0)
            {
                return builder.ToString();
            }
            return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(maxWords));
        }

        private static int CountWords(string value) =>
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
