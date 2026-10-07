using System.Globalization;
using Application.DTOs;

namespace Infrastructure.Services
{
    public class VideoEncodingOptions
    {
        public string StandardResolution { get; set; } = "1280x720";
        public string HdResolution { get; set; } = "1920x1080";
        public int Fps { get; set; } = 30;
        public double MusicVolume { get; set; } = 0.18;
        public string WatermarkText { get; set; } = "Made with Nostalgia AI";
        public string SubtitleFontName { get; set; } = "DejaVu Sans";
        public int FfmpegThreads { get; set; } = 2;
        public int PrescaleStandard { get; set; } = 4;
        public int PrescaleHd { get; set; } = 2;
        public double ZoomAmount { get; set; } = 0.28;
        public double CrossfadeSeconds { get; set; } = 0.8;
        public string FallbackBackgroundColor { get; set; } = "0x2A2422";
        public string Preset { get; set; } = "veryfast";
    }

    public static class FfmpegArgumentBuilder
    {
        public static IReadOnlyList<string> BuildCompose(VideoCompositionRequest request, VideoEncodingOptions options)
        {
            var (width, height) = ParseResolution(request.HighDefinition ? options.HdResolution : options.StandardResolution);
            var fps = Math.Max(1, options.Fps);
            var duration = Math.Max(1.0, request.DurationSeconds);
            var args = new List<string>
            {
                "-nostdin",
                "-hide_banner",
                "-loglevel", "error",
                "-y"
            };
            var inputIndex = 0;
            var images = request.ImageFileNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            if (images.Count > 0)
            {
                // Each still is a single-frame input; zoompan stretches it to its segment length.
                foreach (var image in images)
                {
                    args.Add("-i");
                    args.Add(image);
                    inputIndex++;
                }
            }
            else
            {
                args.Add("-f");
                args.Add("lavfi");
                args.Add("-i");
                args.Add($"color=c={options.FallbackBackgroundColor}:s={width}x{height}:r={fps}:d={Num(duration)}");
                inputIndex++;
            }
            int? voiceInput = null;
            if (!string.IsNullOrWhiteSpace(request.VoiceoverFileName))
            {
                args.Add("-i");
                args.Add(request.VoiceoverFileName!);
                voiceInput = inputIndex++;
            }
            int? musicInput = null;
            if (!string.IsNullOrWhiteSpace(request.MusicFileName))
            {
                args.Add("-stream_loop");
                args.Add("-1");
                args.Add("-i");
                args.Add(request.MusicFileName!);
                musicInput = inputIndex++;
            }
            int? silenceInput = null;
            if (voiceInput == null && musicInput == null)
            {
                args.Add("-f");
                args.Add("lavfi");
                args.Add("-i");
                args.Add("anullsrc=cl=stereo:r=48000");
                silenceInput = inputIndex++;
            }
            var chains = new List<string>
            {
                BuildVideoChain(request, options, images.Count, width, height, fps, duration)
            };
            chains.Add(BuildAudioChain(options, voiceInput, musicInput, silenceInput, duration));

            args.Add("-filter_complex");
            args.Add(string.Join(";", chains));

            args.Add("-map");
            args.Add("[vout]");
            args.Add("-map");
            args.Add("[aout]");

            args.Add("-t");
            args.Add(Num(duration));
            args.Add("-r");
            args.Add(fps.ToString(CultureInfo.InvariantCulture));

            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add(ResolvePreset(options.Preset));
            args.Add("-crf");
            args.Add(request.HighDefinition ? "20" : "23");

            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-profile:v");
            args.Add("high");
            args.Add("-level");
            args.Add("4.0");

            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add("128k");
            args.Add("-ar");
            args.Add("48000");
            args.Add("-ac");
            args.Add("2");

            args.Add("-movflags");
            args.Add("+faststart");

            args.Add("-threads");
            args.Add(Math.Max(1, options.FfmpegThreads).ToString(CultureInfo.InvariantCulture));

            args.Add(request.OutputFileName);

            return args;
        }

        public static IReadOnlyList<string> BuildThumbnail(
            string videoFileName,
            string thumbnailFileName,
            double atSeconds)
        {
            return new List<string>
            {
                "-nostdin",
                "-hide_banner",
                "-loglevel", "error",
                "-y",
                "-ss", Num(Math.Max(0, atSeconds)),
                "-i", videoFileName,
                "-frames:v", "1",
                "-vf", "scale=640:-2",
                "-q:v", "3",
                thumbnailFileName
            };
        }

        public static IReadOnlyList<string> BuildProbeDuration(string videoFileName)
        {
            return new List<string>
            {
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=nw=1:nk=1",
                videoFileName
            };
        }

        private static string BuildVideoChain(
            VideoCompositionRequest request,
            VideoEncodingOptions options,
            int imageCount,
            int width,
            int height,
            int fps,
            double duration)
        {
            var chains = new List<string>();
            string current;
            if (imageCount == 0)
            {
                // The colour source is input 0; Ken Burns makes no sense on a flat colour.
                current = "[0:v]";
            }
            else
            {
                // n segments overlapping by `fade` seconds must add up to the full duration.
                var fade = imageCount > 1
                    ? Math.Min(options.CrossfadeSeconds, duration / imageCount / 3)
                    : 0;
                var segment = (duration + (imageCount - 1) * fade) / imageCount;
                var segmentFrames = Math.Max(1, (int)Math.Ceiling(segment * fps));
                // A full zoom on every short slide feels rushed, so slideshows zoom less per slide.
                var zoomAmount = imageCount == 1 ? options.ZoomAmount : options.ZoomAmount * 0.6;

                for (var i = 0; i < imageCount; i++)
                {
                    chains.Add($"[{i}:v]{KenBurns(request, options, width, height, fps, segmentFrames, zoomAmount, zoomIn: i % 2 == 0)}[s{i}]");
                }

                current = "[s0]";
                for (var k = 1; k < imageCount; k++)
                {
                    var offset = k * (segment - fade);
                    chains.Add($"{current}[s{k}]xfade=transition=fade:duration={Num(fade)}:offset={Num(offset)}[x{k}]");
                    current = $"[x{k}]";
                }
            }

            var filters = new List<string> { "format=yuv420p" };

            if (!string.IsNullOrWhiteSpace(request.CaptionsFileName))
            {
                var fontSize = Math.Max(16, height / 26);
                var marginV = Math.Max(20, height / 12);
                var style =
                    $"FontName={Sanitize(options.SubtitleFontName)}," +
                    $"FontSize={fontSize}," +
                    "PrimaryColour=&H00FFFFFF,OutlineColour=&HA0000000," +
                    "BorderStyle=3,Outline=2,Shadow=0,Alignment=2," +
                    $"MarginV={marginV}";
                filters.Add($"subtitles={request.CaptionsFileName}:fontsdir=.:force_style='{style}'");
            }

            if (request.AddWatermark)
            {
                var fontSize = Math.Max(12, height / 45);
                filters.Add(
                    $"drawtext=fontfile={request.FontFileName}" +
                    $":text='{Sanitize(options.WatermarkText)}'" +
                    $":fontsize={fontSize}:fontcolor=white@0.8" +
                    ":box=1:boxcolor=black@0.35:boxborderw=8" +
                    ":x=w-tw-24:y=h-th-24");
            }
            chains.Add($"{current}{string.Join(",", filters)}[vout]");
            return string.Join(";", chains);
        }

        // Rendered above the output size and scaled back down so the slow zoom doesn't shimmer.
        private static string KenBurns(
            VideoCompositionRequest request,
            VideoEncodingOptions options,
            int width,
            int height,
            int fps,
            int frames,
            double zoomAmount,
            bool zoomIn)
        {
            var prescale = Math.Max(1, request.HighDefinition ? options.PrescaleHd : options.PrescaleStandard);
            var wideWidth = width * prescale;
            var wideHeight = height * prescale;
            var zoomPerFrame = Num(zoomAmount / frames, "0.#########");
            var maxZoom = Num(1 + zoomAmount, "0.####");
            // Alternating zoom in and out keeps a slideshow from feeling repetitive.
            var zoom = zoomIn
                ? $"min(1+{zoomPerFrame}*on,{maxZoom})"
                : $"max({maxZoom}-{zoomPerFrame}*on,1)";
            return
                $"scale={wideWidth}:{wideHeight}:force_original_aspect_ratio=increase," +
                $"crop={wideWidth}:{wideHeight}," +
                $"zoompan=z='{zoom}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'" +
                $":d={frames}:s={width}x{height}:fps={fps}," +
                // xfade needs every segment in the same pixel format and aspect ratio.
                "setsar=1,format=yuv420p";
        }

        private static string BuildAudioChain(
            VideoEncodingOptions options,
            int? voiceInput,
            int? musicInput,
            int? silenceInput,
            double duration)
        {
            if (voiceInput.HasValue && musicInput.HasValue)
            {
                return
                    $"[{voiceInput}:a]aresample=48000,apad[va];" +
                    $"[{musicInput}:a]{MusicFilters(options, duration)}[ma];" +
                    "[va][ma]amix=inputs=2:duration=longest:normalize=0[aout]";
            }

            if (voiceInput.HasValue)
            {
                return $"[{voiceInput}:a]aresample=48000,apad[aout]";
            }

            if (musicInput.HasValue)
            {
                return $"[{musicInput}:a]{MusicFilters(options, duration)}[aout]";
            }

            return $"[{silenceInput}:a]anull[aout]";
        }

        private static string MusicFilters(VideoEncodingOptions options, double duration)
        {
            var fadeOutStart = Math.Max(0, duration - 2);
            return
                $"aresample=48000,volume={Num(options.MusicVolume)}," +
                $"afade=t=in:st=0:d=1.5,afade=t=out:st={Num(fadeOutStart)}:d=2";
        }

        private static readonly HashSet<string> X264Presets = new(StringComparer.OrdinalIgnoreCase)
        {
            "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"
        };

        public static string ResolvePreset(string? preset) =>
            !string.IsNullOrWhiteSpace(preset) && X264Presets.Contains(preset.Trim())
                ? preset.Trim().ToLowerInvariant()
                : "veryfast";

        private static (int Width, int Height) ParseResolution(string value)
        {
            var parts = (value ?? string.Empty).ToLowerInvariant().Split('x');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var width) &&
                int.TryParse(parts[1], out var height) &&
                width > 0 && height > 0)
            {
                return (MakeEven(width), MakeEven(height));
            }
            return (1280, 720);
        }

        private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;

        private static string Num(double value, string format = "0.###") =>
            value.ToString(format, CultureInfo.InvariantCulture);

        private static string Sanitize(string value) =>
            new(value.Where(c => c != '\'' && c != ':' && c != '\\' && c != ',' && c != ';').ToArray());
    }
}
