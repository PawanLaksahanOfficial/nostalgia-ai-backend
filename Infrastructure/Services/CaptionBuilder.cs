using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Application.DTOs;

namespace Infrastructure.Services
{
    public static class CaptionBuilder
    {
        public const int DefaultMaxWordsPerCue = 8;
        public const double DefaultMaxSecondsPerCue = 3.0;

        private static readonly Regex TimingLine = new(
            @"(?<start>\d{1,2}:\d{2}:\d{2}[.,]\d{1,3}|\d{1,2}:\d{2}[.,]\d{1,3})\s*-->\s*(?<end>\d{1,2}:\d{2}:\d{2}[.,]\d{1,3}|\d{1,2}:\d{2}[.,]\d{1,3})",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

        public static List<CaptionCue> ParseWebVtt(string? vtt)
        {
            var cues = new List<CaptionCue>();
            if (string.IsNullOrWhiteSpace(vtt))
            {
                return cues;
            }
            var lines = vtt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var match = TimingLine.Match(lines[i]);
                if (!match.Success)
                {
                    continue;
                }
                var text = new StringBuilder();
                for (var j = i + 1; j < lines.Length && !string.IsNullOrWhiteSpace(lines[j]); j++)
                {
                    if (text.Length > 0)
                    {
                        text.Append(' ');
                    }
                    text.Append(lines[j].Trim());
                    i = j;
                }
                var content = WhitespaceRun.Replace(text.ToString().Trim(), " ");
                if (content.Length == 0)
                {
                    continue;
                }
                cues.Add(new CaptionCue
                {
                    StartSeconds = ParseTimestamp(match.Groups["start"].Value),
                    EndSeconds = ParseTimestamp(match.Groups["end"].Value),
                    Text = content
                });
            }
            return cues;
        }

        public static List<CaptionCue> Regroup(
            IEnumerable<CaptionCue> cues,
            int maxWords = DefaultMaxWordsPerCue,
            double maxSeconds = DefaultMaxSecondsPerCue)
        {
            var grouped = new List<CaptionCue>();
            CaptionCue? current = null;
            var wordsInCurrent = 0;

            foreach (var cue in cues.OrderBy(c => c.StartSeconds))
            {
                var words = CountWords(cue.Text);
                if (current == null)
                {
                    current = new CaptionCue
                    {
                        StartSeconds = cue.StartSeconds,
                        EndSeconds = cue.EndSeconds,
                        Text = cue.Text
                    };
                    wordsInCurrent = words;
                }
                else if (wordsInCurrent + words <= maxWords &&
                         cue.EndSeconds - current.StartSeconds <= maxSeconds)
                {
                    current.Text = $"{current.Text} {cue.Text}";
                    current.EndSeconds = cue.EndSeconds;
                    wordsInCurrent += words;
                }
                else
                {
                    grouped.Add(current);
                    current = new CaptionCue
                    {
                        StartSeconds = cue.StartSeconds,
                        EndSeconds = cue.EndSeconds,
                        Text = cue.Text
                    };
                    wordsInCurrent = words;
                }
                if (current != null && EndsSentence(current.Text))
                {
                    grouped.Add(current);
                    current = null;
                    wordsInCurrent = 0;
                }
            }
            if (current != null)
            {
                grouped.Add(current);
            }
            return grouped;
        }

        public static List<CaptionCue> EvenlyDistribute(
            string? text,
            double totalSeconds,
            int maxWords = DefaultMaxWordsPerCue)
        {
            var cues = new List<CaptionCue>();
            if (string.IsNullOrWhiteSpace(text) || totalSeconds <= 0)
            {
                return cues;
            }
            var words = WhitespaceRun.Replace(text.Trim(), " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return cues;
            }
            var lines = new List<string>();
            var buffer = new List<string>();
            foreach (var word in words)
            {
                buffer.Add(word);
                if (buffer.Count >= maxWords || EndsSentence(word))
                {
                    lines.Add(string.Join(' ', buffer));
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0)
            {
                lines.Add(string.Join(' ', buffer));
            }
            var totalWords = lines.Sum(CountWords);
            var cursor = 0.0;

            for (var i = 0; i < lines.Count; i++)
            {
                var share = (double)CountWords(lines[i]) / totalWords;
                var end = i == lines.Count - 1 ? totalSeconds : cursor + (share * totalSeconds);
                cues.Add(new CaptionCue
                {
                    StartSeconds = cursor,
                    EndSeconds = end,
                    Text = lines[i]
                });
                cursor = end;
            }
            return cues;
        }

        public static string ToSrt(IEnumerable<CaptionCue> cues)
        {
            var builder = new StringBuilder();
            var index = 1;

            foreach (var cue in cues)
            {
                if (string.IsNullOrWhiteSpace(cue.Text))
                {
                    continue;
                }
                builder.Append(index++).Append('\n');
                builder.Append(FormatSrtTimestamp(cue.StartSeconds))
                       .Append(" --> ")
                       .Append(FormatSrtTimestamp(cue.EndSeconds))
                       .Append('\n');
                builder.Append(cue.Text.Trim()).Append("\n\n");
            }
            return builder.ToString();
        }

        private static double ParseTimestamp(string value)
        {
            var normalized = value.Replace(',', '.');
            var parts = normalized.Split(':');
            var hours = parts.Length == 3 ? double.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
            var minutes = double.Parse(parts[^2], CultureInfo.InvariantCulture);
            var seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
            return (hours * 3600) + (minutes * 60) + seconds;
        }

        private static string FormatSrtTimestamp(double totalSeconds)
        {
            if (totalSeconds < 0)
            {
                totalSeconds = 0;
            }
            var span = TimeSpan.FromSeconds(totalSeconds);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00},{3:000}",
                (int)span.TotalHours, span.Minutes, span.Seconds, span.Milliseconds);
        }

        private static bool EndsSentence(string text)
        {
            var trimmed = text.TrimEnd('"', '\'', ')', ']', '”', '’');
            return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?');
        }

        private static int CountWords(string value) =>
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
