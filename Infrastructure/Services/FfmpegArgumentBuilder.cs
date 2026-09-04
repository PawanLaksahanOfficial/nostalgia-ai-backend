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
        public string FallbackBackgroundColor { get; set; } = "0x2A2422";
    }

    public static class FfmpegArgumentBuilder
    {
        public static IReadOnlyList<string> BuildCompose(VideoCompositionRequest request, VideoEncodingOptions options)
        {
            var (width, height) = ParseResolution(request.HighDefinition ? options.HdResolution : options.StandardResolution);
            var fps = Math.Max(1, options.Fps);
            var duration = Math.Max(1.0, request.DurationSeconds);
            var totalFrames = Math.Max(1, (int)Math.Round(duration * fps));
            var args = new List<string>
            {
                "-nostdin",
                "-hide_banner",
                "-loglevel", "error",
                "-y"
            };
            var inputIndex = 0;
            var hasImage = !string.IsNullOrWhiteSpace(request.ImageFileName);
            if (hasImage)
            {
                args.Add("-i");
                args.Add(request.ImageFileName!);
            }
            else
            {
                args.Add("-f");
                args.Add("lavfi");
                args.Add("-i");
                args.Add($"color=c={options.FallbackBackgroundColor}:s={width}x{height}:r={fps}:d={Num(duration)}");
            }
            var videoInput = inputIndex++;
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
                BuildVideoChain(request, options, videoInput, width, height, fps, totalFrames, hasImage)
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
            args.Add("veryfast");
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
            int videoInput,
            int width,
            int height,
            int fps,
            int totalFrames,
            bool hasImage)
        {
            var filters = new List<string>();
            if (hasImage)
            {
                var prescale = Math.Max(1, request.HighDefinition ? options.PrescaleHd : options.PrescaleStandard);
                var wideWidth = width * prescale;
                var wideHeight = height * prescale;
                filters.Add($"scale={wideWidth}:{wideHeight}:force_original_aspect_ratio=increase");
                filters.Add($"crop={wideWidth}:{wideHeight}");
                var zoomPerFrame = options.ZoomAmount / totalFrames;
                var maxZoom = 1 + options.ZoomAmount;
                filters.Add(
                    $"zoompan=z='min(1+{Num(zoomPerFrame, "0.#########")}*on,{Num(maxZoom, "0.####")})'" +
                    $":x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'" +
                    $":d={totalFrames}:s={width}x{height}:fps={fps}");
            }

            filters.Add("format=yuv420p");

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
            return $"[{videoInput}:v]{string.Join(",", filters)}[vout]";
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
