using Application.DTOs;
using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class CaptionBuilderTests
    {
        private const string SampleVtt = @"WEBVTT

00:00:00.100 --> 00:00:00.400
The

00:00:00.400 --> 00:00:00.900
summer

00:00:00.900 --> 00:00:01.300
was

00:00:01.300 --> 00:00:01.800
warm.

00:00:01.800 --> 00:00:02.400
We

00:00:02.400 --> 00:00:03.000
laughed.
";

        [Fact]
        public void Parses_every_cue_from_web_vtt()
        {
            var cues = CaptionBuilder.ParseWebVtt(SampleVtt);

            Assert.Equal(6, cues.Count);
            Assert.Equal("The", cues[0].Text);
            Assert.Equal(0.1, cues[0].StartSeconds, 3);
            Assert.Equal(3.0, cues[^1].EndSeconds, 3);
        }

        [Fact]
        public void Parsing_empty_input_returns_no_cues()
        {
            Assert.Empty(CaptionBuilder.ParseWebVtt(null));
            Assert.Empty(CaptionBuilder.ParseWebVtt("   "));
        }

        [Fact]
        public void Regroup_merges_word_cues_into_readable_lines()
        {
            var cues = CaptionBuilder.Regroup(CaptionBuilder.ParseWebVtt(SampleVtt));

            // Six single words collapse into two sentence-aligned lines.
            Assert.Equal(2, cues.Count);
            Assert.Equal("The summer was warm.", cues[0].Text);
            Assert.Equal("We laughed.", cues[1].Text);
        }

        [Fact]
        public void Regroup_breaks_a_line_once_it_reaches_the_word_limit()
        {
            var words = Enumerable.Range(0, 20).Select(i => new CaptionCue
            {
                StartSeconds = i * 0.2,
                EndSeconds = (i * 0.2) + 0.2,
                Text = $"word{i}"
            });

            var cues = CaptionBuilder.Regroup(words, maxWords: 4, maxSeconds: 60);

            Assert.All(cues, cue =>
                Assert.True(cue.Text.Split(' ').Length <= 4, $"Line too long: '{cue.Text}'"));
        }

        [Fact]
        public void Regrouped_cues_never_overlap()
        {
            var cues = CaptionBuilder.Regroup(CaptionBuilder.ParseWebVtt(SampleVtt));

            for (var i = 1; i < cues.Count; i++)
            {
                Assert.True(cues[i].StartSeconds >= cues[i - 1].EndSeconds,
                    "Cues overlap, which would show two captions at once.");
            }
        }

        [Fact]
        public void Even_distribution_covers_the_whole_clip()
        {
            var cues = CaptionBuilder.EvenlyDistribute(
                "One two three four five six seven eight nine ten eleven twelve.", 30);

            Assert.NotEmpty(cues);
            Assert.Equal(0, cues[0].StartSeconds);
            Assert.Equal(30, cues[^1].EndSeconds, 3);
        }

        [Fact]
        public void Even_distribution_of_empty_text_returns_no_cues()
        {
            Assert.Empty(CaptionBuilder.EvenlyDistribute("", 30));
            Assert.Empty(CaptionBuilder.EvenlyDistribute("something", 0));
        }

        [Fact]
        public void Srt_uses_comma_decimals_and_one_based_indices()
        {
            var srt = CaptionBuilder.ToSrt(new[]
            {
                new CaptionCue { StartSeconds = 0, EndSeconds = 1.5, Text = "Hello" },
                new CaptionCue { StartSeconds = 1.5, EndSeconds = 3, Text = "World" }
            });

            Assert.StartsWith("1\n", srt);
            Assert.Contains("00:00:00,000 --> 00:00:01,500", srt);
            Assert.Contains("2\n", srt);
            // A period here would make FFmpeg reject the subtitle file.
            Assert.DoesNotContain("00:00:01.500", srt);
        }

        [Fact]
        public void Srt_skips_blank_cues()
        {
            var srt = CaptionBuilder.ToSrt(new[]
            {
                new CaptionCue { StartSeconds = 0, EndSeconds = 1, Text = "   " }
            });

            Assert.Equal(string.Empty, srt);
        }
    }
}
