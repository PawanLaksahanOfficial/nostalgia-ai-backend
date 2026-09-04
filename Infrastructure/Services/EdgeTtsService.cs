using Application.DTOs;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class EdgeTtsService : ITextToSpeech
    {
        private readonly ILogger<EdgeTtsService> _logger;
        private readonly string _edgeTtsPath;
        private readonly string _voice;
        private readonly string _rate;
        private readonly TimeSpan _timeout;

        public EdgeTtsService(IConfiguration configuration, ILogger<EdgeTtsService> logger)
        {
            _logger = logger;
            var section = configuration.GetSection("Tts");
            _edgeTtsPath = section["EdgeTtsPath"] ?? "edge-tts";
            _voice = section["Voice"] ?? "en-US-AriaNeural";
            _rate = section["Rate"] ?? "-5%";
            _timeout = TimeSpan.FromSeconds(section.GetValue("TimeoutSeconds", 90));
        }

        public bool IsEnabled => true;

        public async Task<SpeechResult?> SynthesizeAsync(string text, string outputFilePath)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            var workingDirectory = Path.GetDirectoryName(outputFilePath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return null;
            }
            var audioFileName = Path.GetFileName(outputFilePath);
            var subtitleFileName = Path.ChangeExtension(audioFileName, ".vtt");
            var textFileName = "narrative.txt";
            try
            {
                await File.WriteAllTextAsync(Path.Combine(workingDirectory, textFileName), text);
                var arguments = new[]
                {
                    "--voice", _voice,
                    $"--rate={_rate}",
                    "--file", textFileName,
                    "--write-media", audioFileName,
                    "--write-subtitles", subtitleFileName
                };
                var result = await ProcessRunner.RunAsync(_edgeTtsPath, arguments, workingDirectory, _timeout);
                if (!result.Succeeded)
                {
                    _logger.LogWarning(
                        "edge-tts failed (exit {ExitCode}, timedOut={TimedOut}). Continuing without a voiceover. {Stderr}",
                        result.ExitCode, result.TimedOut, result.StandardError);
                    return null;
                }
                var audioPath = Path.Combine(workingDirectory, audioFileName);
                if (!File.Exists(audioPath) || new FileInfo(audioPath).Length == 0)
                {
                    _logger.LogWarning("edge-tts reported success but wrote no audio. Continuing without a voiceover.");
                    return null;
                }
                var cues = await ReadCuesAsync(Path.Combine(workingDirectory, subtitleFileName));
                return new SpeechResult
                {
                    AudioFilePath = audioPath,
                    DurationSeconds = cues.Count > 0 ? cues[^1].EndSeconds : 0,
                    Cues = cues
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Text-to-speech threw. Continuing without a voiceover.");
                return null;
            }
        }

        private static async Task<List<CaptionCue>> ReadCuesAsync(string subtitlePath)
        {
            if (!File.Exists(subtitlePath))
            {
                return new List<CaptionCue>();
            }
            var vtt = await File.ReadAllTextAsync(subtitlePath);
            return CaptionBuilder.Regroup(CaptionBuilder.ParseWebVtt(vtt));
        }
    }
}
