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
                ImageFileNames = image == null ? new List<string>() : new List<string> { image },
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

        private static VideoCompositionRequest Slideshow(int images, double duration = 20, string? voice = null) => new()
        {
            MemoryId = 1,
            WorkingDirectory = "/tmp/job",
            ImageFileNames = Enumerable.Range(0, images).Select(i => $"img{i}.jpg").ToList(),
            VoiceoverFileName = voice,
            DurationSeconds = duration
        };

        [Fact]
        public void A_single_image_needs_no_crossfade()
        {
            Assert.DoesNotContain("xfade", Build(Request()));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Chains_one_crossfade_between_each_pair_of_slides(int images)
        {
            var args = FfmpegArgumentBuilder.BuildCompose(Slideshow(images), _options);
            var filters = string.Join(" ", args);

            Assert.Equal(images, args.Count(a => a.StartsWith("img", StringComparison.Ordinal)));
            Assert.Equal(images, CountOf(filters, "zoompan="));
            Assert.Equal(images - 1, CountOf(filters, "xfade="));
            Assert.Contains($"[x{images - 1}]format=yuv420p", filters);
        }

        [Fact]
        public void Places_crossfades_so_the_slides_fill_the_whole_duration()
        {
            // 3 slides over 20s with 0.8s fades: each slide is (20 + 2*0.8) / 3 = 7.2s long.
            var filters = Build(Slideshow(3, duration: 20));

            Assert.Contains("[s0][s1]xfade=transition=fade:duration=0.8:offset=6.4[x1]", filters);
            Assert.Contains("[x1][s2]xfade=transition=fade:duration=0.8:offset=12.8[x2]", filters);
        }

        [Fact]
        public void Shortens_the_crossfade_when_slides_are_very_short()
        {
            // 4 slides in 4s: a 0.8s fade would swallow most of each slide.
            var filters = Build(Slideshow(4, duration: 4));

            Assert.Contains("xfade=transition=fade:duration=0.333", filters);
        }

        [Fact]
        public void Numbers_the_audio_inputs_after_every_slide()
        {
            var filters = Build(Slideshow(3, voice: "voice.mp3"));

            // img0..img2 are inputs 0-2, so the voice is input 3.
            Assert.Contains("[3:a]aresample=48000,apad[aout]", filters);
        }

        private static int CountOf(string text, string value) =>
            (text.Length - text.Replace(value, string.Empty).Length) / value.Length;

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

        [Fact]
        public void Uses_veryfast_preset_by_default()
        {
            var args = FfmpegArgumentBuilder.BuildCompose(Request(), _options).ToList();

            Assert.Equal("veryfast", args[args.IndexOf("-preset") + 1]);
        }

        [Fact]
        public void Uses_the_configured_preset()
        {
            var options = new VideoEncodingOptions { Preset = "UltraFast" };
            var args = FfmpegArgumentBuilder.BuildCompose(Request(), options).ToList();

            Assert.Equal("ultrafast", args[args.IndexOf("-preset") + 1]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("turbo")]
        [InlineData("fast -vf evil")]
        public void Falls_back_to_veryfast_for_unknown_presets(string preset)
        {
            Assert.Equal("veryfast", FfmpegArgumentBuilder.ResolvePreset(preset));
        }

        [Fact]
        public void Zooms_in_full_colour_when_configured()
        {
            var options = new VideoEncodingOptions { FullChromaZoom = true };
            var filters = string.Join(" ", FfmpegArgumentBuilder.BuildCompose(Request(), options));

            Assert.Contains("format=yuv444p,zoompan=", filters);
        }

        [Fact]
        public void Pans_instead_of_zooming_when_configured()
        {
            var options = new VideoEncodingOptions { MotionStyle = "pan" };
            var filters = string.Join(" ", FfmpegArgumentBuilder.BuildCompose(Slideshow(2), options));

            Assert.DoesNotContain("zoompan", filters);
            // Each photo is scaled once and repeated, then the crop window moves across it.
            Assert.Contains("loop=loop=", filters);
            Assert.Contains("settb=1/30,setpts=N", filters);
            Assert.Contains("[s0][s1]xfade=", filters);
        }

        [Fact]
        public void Pans_a_whole_number_of_pixels_every_frame_in_alternating_directions()
        {
            // 12 px/s at the default 4x prescale and 30 fps is 1.6 px per frame, rounded to 2.
            var options = new VideoEncodingOptions { MotionStyle = "pan", PanPixelsPerSecond = 12 };
            var filters = string.Join(" ", FfmpegArgumentBuilder.BuildCompose(Slideshow(2, duration: 10), options));

            Assert.Contains("x='n*2'", filters);
            Assert.Matches(@"x='\d+-n\*2'", filters);
        }
    }
}
