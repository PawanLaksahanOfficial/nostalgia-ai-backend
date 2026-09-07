using System.Globalization;
using Application.DTOs;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class FfmpegVideoComposer : IVideoComposer
    {
        private readonly ILogger<FfmpegVideoComposer> _logger;
        private readonly VideoEncodingOptions _options;
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;
        private readonly TimeSpan _jobTimeout;
        private static bool _knownAvailable;

        public FfmpegVideoComposer(IConfiguration configuration, ILogger<FfmpegVideoComposer> logger)
        {
            _logger = logger;

            var section = configuration.GetSection("Video");
            _ffmpegPath = section["FfmpegPath"] ?? "ffmpeg";
            _ffprobePath = section["FfprobePath"] ?? "ffprobe";
            _jobTimeout = TimeSpan.FromSeconds(section.GetValue("JobTimeoutSeconds", 300));

            _options = new VideoEncodingOptions
            {
                StandardResolution = section["StandardResolution"] ?? "1280x720",
                HdResolution = section["HdResolution"] ?? "1920x1080",
                Fps = section.GetValue("Fps", 30),
                MusicVolume = section.GetValue("MusicVolume", 0.18),
                WatermarkText = section["WatermarkText"] ?? "Made with Nostalgia AI",
                SubtitleFontName = section["SubtitleFontName"] ?? "DejaVu Sans",
                FfmpegThreads = section.GetValue("FfmpegThreads", 2),
                PrescaleStandard = section.GetValue("PrescaleStandard", 4),
                PrescaleHd = section.GetValue("PrescaleHd", 2),
                ZoomAmount = section.GetValue("ZoomAmount", 0.28),
                FallbackBackgroundColor = section["FallbackBackgroundColor"] ?? "0x2A2422"
            };
        }

        public async Task<bool> IsAvailableAsync()
        {
            if (_knownAvailable)
            {
                return true;
            }

            try
            {
                var result = await ProcessRunner.RunAsync(
                    _ffmpegPath,
                    new[] { "-version" },
                    workingDirectory: null,
                    timeout: TimeSpan.FromSeconds(10));
                _knownAvailable = result.Succeeded;
                return _knownAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not probe ffmpeg at '{FfmpegPath}'.", _ffmpegPath);
                return false;
            }
        }

        public async Task<ComposedVideo> ComposeAsync(VideoCompositionRequest request)
        {
            var arguments = FfmpegArgumentBuilder.BuildCompose(request, _options);

            _logger.LogInformation("Composing video for memory {MemoryId} ({Duration:0.#}s, hd={Hd}, watermark={Watermark}).", request.MemoryId, request.DurationSeconds, request.HighDefinition, request.AddWatermark);

            var result = await ProcessRunner.RunAsync(_ffmpegPath, arguments, request.WorkingDirectory, _jobTimeout);

            if (result.TimedOut)
            {
                throw new InvalidOperationException($"Video composition timed out after {_jobTimeout.TotalSeconds:0}s.");
            }

            if (!result.Succeeded)
            {
                _logger.LogError("ffmpeg exited with code {ExitCode} for memory {MemoryId}. Output:{NewLine}{Stderr}", result.ExitCode, request.MemoryId, Environment.NewLine, result.StandardError);
                throw new InvalidOperationException($"ffmpeg exited with code {result.ExitCode}.");
            }

            var videoPath = Path.Combine(request.WorkingDirectory, request.OutputFileName);
            if (!File.Exists(videoPath))
            {
                throw new InvalidOperationException("ffmpeg reported success but produced no output file.");
            }

            var thumbnailPath = await TryCreateThumbnailAsync(request);
            var duration = await TryProbeDurationAsync(request.WorkingDirectory, request.OutputFileName) ?? request.DurationSeconds;

            return new ComposedVideo
            {
                VideoFilePath = videoPath,
                ThumbnailFilePath = thumbnailPath,
                DurationSeconds = duration,
                FileSizeBytes = new FileInfo(videoPath).Length,
                ContentType = "video/mp4"
            };
        }

        private async Task<string?> TryCreateThumbnailAsync(VideoCompositionRequest request)
        {
            try
            {
                var arguments = FfmpegArgumentBuilder.BuildThumbnail(
                    request.OutputFileName,
                    request.ThumbnailFileName,
                    request.DurationSeconds * 0.25);
                var result = await ProcessRunner.RunAsync( _ffmpegPath, arguments, request.WorkingDirectory, TimeSpan.FromSeconds(60));
                var path = Path.Combine(request.WorkingDirectory, request.ThumbnailFileName);
                if (result.Succeeded && File.Exists(path))
                {
                    return path;
                }
                _logger.LogWarning("Thumbnail extraction failed for memory {MemoryId}: {Stderr}", request.MemoryId, result.StandardError);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail extraction threw for memory {MemoryId}.", request.MemoryId);
            }

            return null;
        }

        private async Task<double?> TryProbeDurationAsync(string workingDirectory, string fileName)
        {
            try
            {
                var result = await ProcessRunner.RunAsync(
                    _ffprobePath,
                    FfmpegArgumentBuilder.BuildProbeDuration(fileName),
                    workingDirectory,
                    TimeSpan.FromSeconds(30));

                if (result.Succeeded &&
                    double.TryParse(
                        result.StandardOutput.Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var seconds))
                {
                    return seconds;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ffprobe failed; falling back to the requested duration.");
            }

            return null;
        }
    }
}
