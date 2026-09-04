using Application.DTOs;
using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class FfmpegArgumentBuilderTests
    {
        private readonly VideoEncodingOptions _options = new();

        private static VideoCompositionRequest Request(
            string? image = "source.jpg",
            string? voice = null,
            string? music = null,
            string? captions = null,
            double duration = 20,
            bool hd = false,
            bool watermark = false) => new()
            {
                MemoryId = 1,
                WorkingDirectory = "/tmp/job",
                ImageFileName = image,
                VoiceoverFileName = voice,
                MusicFileName = music,
                CaptionsFileName = captions,
                DurationSeconds = duration,
                HighDefinition = hd,
                AddWatermark = watermark
            };

        private string Build(VideoCompositionRequest request) =>
            string.Join(" ", FfmpegArgumentBuilder.BuildCompose(request, _options));

        [Fact]
        public void Never_loops_the_image_input_because_that_makes_zoompan_stutter()
        {
            var args = FfmpegArgumentBuilder.BuildCompose(Request(), _options);
            Assert.DoesNotContain("-loop", args);
        }

        [Fact]
        public void Always_encodes_yuv420p_and_faststart_for_browser_playback()
        {
            var args = FfmpegArgumentBuilder.BuildCompose(Request(), _options);

            Assert.Contains("yuv420p", args);
            Assert.Contains("+faststart", args);
        }

        [Fact]
        public void Uses_hd_resolution_only_when_high_definition_is_requested()
        {
            Assert.Contains("1280x720", Build(Request(hd: false)));
            Assert.Contains("1920x1080", Build(Request(hd: true)));
        }

        [Fact]
        public void Adds_the_watermark_stage_only_when_requested()
        {
            Assert.DoesNotContain("drawtext", Build(Request(watermark: false)));
            Assert.Contains("drawtext", Build(Request(watermark: true)));
        }

        [Fact]
        public void Adds_the_subtitles_stage_only_when_captions_exist()
        {
            Assert.DoesNotContain("subtitles=", Build(Request(captions: null)));
            Assert.Contains("subtitles=captions.srt", Build(Request(captions: "captions.srt")));
        }

        [Fact]
        public void Cuts_the_output_at_the_requested_duration()
        {
            var args = FfmpegArgumentBuilder.BuildCompose(Request(duration: 37), _options);

            var index = args.ToList().IndexOf("-t");
            Assert.True(index >= 0);
            Assert.Equal("37", args[index + 1]);
        }

        [Fact]
        public void Mixes_silence_when_there_is_neither_voice_nor_music()
        {
            var filters = Build(Request());

            Assert.Contains("anullsrc", filters);
            // The silence source is the second input, after the image.
            Assert.Contains("[1:a]anull[aout]", filters);
        }

        [Fact]
        public void Uses_the_voice_input_alone_when_there_is_no_music()
        {
            var filters = Build(Request(voice: "voice.mp3"));

            Assert.DoesNotContain("anullsrc", filters);
            Assert.Contains("[1:a]aresample=48000,apad[aout]", filters);
        }

        [Fact]
        public void Uses_the_music_input_alone_when_there_is_no_voice()
        {
            var filters = Build(Request(music: "music.mp3"));

            Assert.DoesNotContain("anullsrc", filters);
            Assert.Contains("[1:a]aresample=48000,volume=", filters);
            Assert.Contains("-stream_loop", filters);
        }

        [Fact]
        public void Numbers_the_voice_and_music_inputs_in_the_order_they_are_added()
        {
            var filters = Build(Request(voice: "voice.mp3", music: "music.mp3"));

            // image=0, voice=1, music=2
            Assert.Contains("[1:a]aresample=48000,apad[va]", filters);
            Assert.Contains("[2:a]aresample=48000,volume=", filters);
        }

        [Fact]
        public void Disables_amix_normalization_so_the_voice_is_not_halved()
        {
            var filters = Build(Request(voice: "voice.mp3", music: "music.mp3"));

            Assert.Contains("amix=", filters);
            Assert.Contains("normalize=0", filters);
        }

        [Fact]
        public void Falls_back_to_a_generated_background_when_there_is_no_image()
        {
            var filters = Build(Request(image: null));

            Assert.Contains("color=c=", filters);
            // Ken Burns makes no sense on a flat colour, so it is skipped entirely.
            Assert.DoesNotContain("zoompan", filters);
        }

        [Fact]
        public void Applies_ken_burns_when_an_image_is_supplied()
        {
            var filters = Build(Request());

            Assert.Contains("zoompan", filters);
            // Rendered above output size, then scaled back down, to stop it shimmering.
            Assert.Contains("scale=5120:2880", filters);
        }

        [Fact]
        public void Formats_numbers_invariantly_regardless_of_the_current_culture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // German uses "," as the decimal separator, which would split filters.
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var filters = Build(Request(music: "music.mp3", duration: 12.5));

                Assert.Contains("volume=0.18", filters);
                Assert.DoesNotContain("volume=0,18", filters);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Fact]
        public void Thumbnail_seeks_before_the_input_so_it_does_not_decode_the_whole_file()
        {
            var args = FfmpegArgumentBuilder.BuildThumbnail("final.mp4", "thumb.jpg", 5).ToList();

            Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
            Assert.Contains("thumb.jpg", args);
        }
    }
}
