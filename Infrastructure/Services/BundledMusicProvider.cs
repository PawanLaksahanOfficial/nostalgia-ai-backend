using Application.DTOs;
using Application.Interfaces;
using Application.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class BundledMusicProvider : IMusicProvider
    {
        private static readonly string[] SupportedExtensions = { ".mp3", ".m4a", ".ogg", ".wav" };
        private readonly ILogger<BundledMusicProvider> _logger;
        private readonly string _musicDirectory;

        // A job runs every few seconds; warn once rather than filling the log.
        private static bool _warnedAboutMissingLibrary;

        public BundledMusicProvider(IConfiguration configuration, ILogger<BundledMusicProvider> logger)
        {
            _logger = logger;
            var assetsPath = configuration.GetSection("Video")["AssetsPath"] ?? "assets";
            if (!Path.IsPathRooted(assetsPath))
            {
                assetsPath = Path.Combine(AppContext.BaseDirectory, assetsPath);
            }
            _musicDirectory = Path.Combine(assetsPath, "music");
        }

        public IReadOnlyList<string> AvailableMoods => MusicMoods.Allowed;

        public Task<MusicTrack?> GetTrackAsync(string? mood)
        {
            if (!Directory.Exists(_musicDirectory))
            {
                WarnOnce();
                return Task.FromResult<MusicTrack?>(null);
            }
            var requested = MusicMoods.IsAllowed(mood)
                ? mood!.Trim().ToLowerInvariant()
                : MusicMoods.Default;
            var track = FindTrack(requested) ?? FindTrack(MusicMoods.Default) ?? FindAnyTrack();
            if (track == null)
            {
                WarnOnce();
                return Task.FromResult<MusicTrack?>(null);
            }
            return Task.FromResult<MusicTrack?>(new MusicTrack
            {
                Name = Path.GetFileNameWithoutExtension(track),
                FilePath = track
            });
        }

        private string? FindTrack(string mood) =>
            SupportedExtensions
                .Select(extension => Path.Combine(_musicDirectory, mood + extension))
                .FirstOrDefault(File.Exists);

        private string? FindAnyTrack() =>
            Directory.EnumerateFiles(_musicDirectory)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .OrderBy(file => file)
                .FirstOrDefault();

        private void WarnOnce()
        {
            if (_warnedAboutMissingLibrary)
            {
                return;
            }
            _warnedAboutMissingLibrary = true;
            _logger.LogWarning("No music tracks found in '{MusicDirectory}'. Videos will be composed without a music bed.", _musicDirectory);
        }
    }
}
