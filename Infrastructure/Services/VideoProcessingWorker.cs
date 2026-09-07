using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class VideoProcessingWorker : BackgroundService
    {
        private const string ImageFileName = "source";
        private const string VoiceFileName = "voice.mp3";
        private const string MusicFileNamePrefix = "music";
        private const string CaptionsFileName = "captions.srt";
        private const string OutputFileName = "final.mp4";
        private const string ThumbnailFileName = "thumb.jpg";
        private const string FontFileName = "font.ttf";
        private static readonly TimeSpan UnavailableProbeInterval = TimeSpan.FromMinutes(5);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VideoProcessingWorker> _logger;
        private readonly TimeSpan _pollingInterval;
        private readonly TimeSpan _staleJobTimeout;
        private readonly int _maxConcurrentJobs;
        private readonly int _minDurationSeconds;
        private readonly bool _keepIntermediates;
        private readonly string _fontPath;
        private DateTime _lastUnavailableWarning = DateTime.MinValue;
        private DateTime _lastStaleSweep = DateTime.MinValue;

        public VideoProcessingWorker(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<VideoProcessingWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            var section = configuration.GetSection("Video");
            _pollingInterval = TimeSpan.FromSeconds(section.GetValue("PollSeconds", 10));
            _staleJobTimeout = TimeSpan.FromMinutes(section.GetValue("StaleJobTimeoutMinutes", 20));
            _maxConcurrentJobs = Math.Max(1, section.GetValue("MaxConcurrentJobs", 1));
            _minDurationSeconds = Math.Max(4, section.GetValue("MinDurationSeconds", 8));
            _keepIntermediates = section.GetValue("KeepIntermediates", false);

            var assetsPath = section["AssetsPath"] ?? "assets";
            if (!Path.IsPathRooted(assetsPath))
            {
                assetsPath = Path.Combine(AppContext.BaseDirectory, assetsPath);
            }
            _fontPath = Path.Combine(assetsPath, "fonts", section["FontFileName"] ?? "DejaVuSans.ttf");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Video Processing Worker started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing video jobs.");
                }

                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            _logger.LogInformation("Video Processing Worker stopping.");
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var composer = scope.ServiceProvider.GetRequiredService<IVideoComposer>();
            var memoryRepository = scope.ServiceProvider.GetRequiredService<IMemoryRepository>();

            if (!await composer.IsAvailableAsync())
            {
                if (DateTime.UtcNow - _lastUnavailableWarning > UnavailableProbeInterval)
                {
                    _lastUnavailableWarning = DateTime.UtcNow;
                    _logger.LogCritical(
                        "ffmpeg is not available. Video jobs will remain queued until it is installed and reachable.");
                }
                return;
            }
            await SweepStaleJobsAsync(memoryRepository);
            var candidates = await memoryRepository.GetPendingAsync(_maxConcurrentJobs);
            var running = new List<Task>();
            foreach (var candidate in candidates)
            {
                if (!await memoryRepository.TryClaimForProcessingAsync(candidate.Id))
                {
                    continue;
                }
                running.Add(ProcessClaimedJobAsync(candidate.Id, cancellationToken));
            }
            if (running.Count > 0)
            {
                await Task.WhenAll(running);
            }
        }

        private async Task SweepStaleJobsAsync(IMemoryRepository memoryRepository)
        {
            if (DateTime.UtcNow - _lastStaleSweep < _staleJobTimeout)
            {
                return;
            }
            _lastStaleSweep = DateTime.UtcNow;
            var requeued = await memoryRepository.RequeueStaleProcessingAsync(_staleJobTimeout);
            if (requeued > 0)
            {
                _logger.LogWarning("Requeued {Count} job(s) left mid-encode by a previous run.", requeued);
            }
        }

        private async Task ProcessClaimedJobAsync(int memoryId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var dbContext = services.GetRequiredService<EFDbContext>();
            var job = await dbContext.UserMemories.FirstOrDefaultAsync(m => m.Id == memoryId, cancellationToken);
            if (job == null)
            {
                return;
            }
            var workingDirectory = Path.Combine(Path.GetTempPath(), "nostalgia-ai", $"job-{memoryId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workingDirectory);
            try
            {
                await RunPipelineAsync(job, workingDirectory, services, dbContext, cancellationToken);
                _logger.LogInformation("Job {JobId} completed successfully.", job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process job {JobId}.", job.Id);
                job.Status = VideoStatus.Failed;
                job.FailureReason = "We couldn't finish creating your video. Please try again.";
                job.ProcessingStep = null;
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            finally
            {
                CleanUp(workingDirectory);
            }
        }

        private async Task RunPipelineAsync(
            UserMemory job,
            string workingDirectory,
            IServiceProvider services,
            EFDbContext dbContext,
            CancellationToken cancellationToken)
        {
            var aiService = services.GetRequiredService<IAIService>();
            var musicProvider = services.GetRequiredService<IMusicProvider>();
            var textToSpeech = services.GetRequiredService<ITextToSpeech>();
            var composer = services.GetRequiredService<IVideoComposer>();
            var fileStorage = services.GetRequiredService<IFileStorage>();
            var subscriptionService = services.GetRequiredService<ISubscriptionService>();
            var quota = await subscriptionService.GetUsageQuotaAsync(job.UserId);
            var maxDuration = Math.Max(_minDurationSeconds, quota.MaxVideoDurationSeconds);
            var highDefinition = string.Equals(quota.Quality, "hd", StringComparison.OrdinalIgnoreCase);
            var wordBudget = NarrativeTrimmer.WordBudgetForSeconds(maxDuration);
            //  Step 1: narrative 
            await SetStepAsync(dbContext, job, "Writing your story…", cancellationToken);
            var narrative = await GenerateNarrativeAsync(aiService, job, wordBudget);
            job.GeneratedNarrative = narrative;

            // Step 2: music 
            await SetStepAsync(dbContext, job, "Picking the music…", cancellationToken);
            var musicFileName = await CopyMusicAsync(musicProvider, job, workingDirectory);

            //  Step 3: narration 
            await SetStepAsync(dbContext, job, "Recording the narration…", cancellationToken);
            var speech = await textToSpeech.SynthesizeAsync(narrative, Path.Combine(workingDirectory, VoiceFileName));

            //  Step 4: composition 
            await SetStepAsync(dbContext, job, "Composing your video…", cancellationToken);
            var imageFileName = await FetchImageAsync(fileStorage, job, workingDirectory);
            var fontAvailable = TryCopyFont(workingDirectory);
            var spokenSeconds = speech is { DurationSeconds: > 0 }
                ? speech.DurationSeconds
                : EstimateSpokenSeconds(narrative);
            var duration = Math.Clamp(Math.Ceiling(spokenSeconds + 2), _minDurationSeconds, maxDuration);
            var captionsFileName = WriteCaptions(workingDirectory, speech, narrative, duration);
            var composed = await composer.ComposeAsync(new Application.DTOs.VideoCompositionRequest
            {
                MemoryId = job.Id,
                WorkingDirectory = workingDirectory,
                ImageFileName = imageFileName,
                VoiceoverFileName = speech != null ? VoiceFileName : null,
                MusicFileName = musicFileName,
                CaptionsFileName = captionsFileName,
                OutputFileName = OutputFileName,
                ThumbnailFileName = ThumbnailFileName,
                FontFileName = FontFileName,
                DurationSeconds = duration,
                HighDefinition = highDefinition,
                AddWatermark = quota.HasWatermark && fontAvailable
            });

            //  Step 5: publish 
            await SetStepAsync(dbContext, job, "Finishing up…", cancellationToken);
            job.FinalVideoPath = await StoreAsync(fileStorage, $"memories/{job.Id}/{OutputFileName}", composed.VideoFilePath, composed.ContentType);
            if (!string.IsNullOrEmpty(composed.ThumbnailFilePath))
            {
                job.ThumbnailPath = await StoreAsync(
                    fileStorage, $"memories/{job.Id}/{ThumbnailFileName}", composed.ThumbnailFilePath, "image/jpeg");
            }
            if (captionsFileName != null)
            {
                job.CaptionsPath = await StoreAsync(
                    fileStorage,
                    $"memories/{job.Id}/{CaptionsFileName}",
                    Path.Combine(workingDirectory, captionsFileName),
                    "text/plain");
            }
            job.DurationSeconds = composed.DurationSeconds;
            job.FileSizeBytes = composed.FileSizeBytes;
            job.ContentType = composed.ContentType;
            job.Status = VideoStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessingStep = null;
            job.FailureReason = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task<string> GenerateNarrativeAsync(IAIService aiService, UserMemory job, int wordBudget)
        {
            var prompt =
                $"Retell this memory as a warm, nostalgic narration of about {wordBudget} words. " +
                $"Write flowing prose with no headings or lists.\n\n{job.StoryText}";
            string? narrative = null;
            try
            {
                narrative = await aiService.GenerateNostalgicTextAsync(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Narrative generation threw for job {JobId}.", job.Id);
            }
            if (string.IsNullOrWhiteSpace(narrative))
            {
                _logger.LogWarning("No narrative was generated for job {JobId}; falling back to the submitted story.", job.Id);
                narrative = job.StoryText;
            }
            return NarrativeTrimmer.Trim(narrative, wordBudget);
        }

        private async Task<string?> CopyMusicAsync(
            IMusicProvider musicProvider, UserMemory job, string workingDirectory)
        {
            var track = await musicProvider.GetTrackAsync(job.MusicMood);
            if (track == null || !File.Exists(track.FilePath))
            {
                return null;
            }
            var fileName = MusicFileNamePrefix + Path.GetExtension(track.FilePath);
            File.Copy(track.FilePath, Path.Combine(workingDirectory, fileName), overwrite: true);
            job.MusicTrackName = track.Name;
            return fileName;
        }

        private async Task<string?> FetchImageAsync(IFileStorage fileStorage, UserMemory job, string workingDirectory)
        {
            if (string.IsNullOrEmpty(job.UserImagePath))
            {
                return null;
            }
            try
            {
                await using var source = await fileStorage.DownloadAsync(job.UserImagePath);
                if (source == null)
                {
                    _logger.LogWarning(
                        "Image '{Key}' is missing for job {JobId}; composing without it.",
                        job.UserImagePath, job.Id);
                    return null;
                }
                var fileName = ImageFileName + (Path.GetExtension(job.UserImagePath) is { Length: > 0 } ext
                    ? ext
                    : ".jpg");
                await using var destination = File.Create(Path.Combine(workingDirectory, fileName));
                await source.CopyToAsync(destination);
                return fileName;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch the image for job {JobId}; composing without it.", job.Id);
                return null;
            }
        }

        private bool TryCopyFont(string workingDirectory)
        {
            try
            {
                if (!File.Exists(_fontPath))
                {
                    _logger.LogWarning("Watermark font '{FontPath}' not found; composing without a watermark.", _fontPath);
                    return false;
                }
                File.Copy(_fontPath, Path.Combine(workingDirectory, FontFileName), overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not stage the watermark font; composing without a watermark.");
                return false;
            }
        }

        private static string? WriteCaptions(
            string workingDirectory,
            Application.DTOs.SpeechResult? speech,
            string narrative,
            double duration)
        {
            var cues = speech is { Cues.Count: > 0 }
                ? speech.Cues
                : CaptionBuilder.EvenlyDistribute(narrative, duration);
            var srt = CaptionBuilder.ToSrt(cues);
            if (string.IsNullOrWhiteSpace(srt))
            {
                return null;
            }
            File.WriteAllText(Path.Combine(workingDirectory, CaptionsFileName), srt);
            return CaptionsFileName;
        }

        private static async Task<string> StoreAsync(
            IFileStorage fileStorage, string key, string sourcePath, string contentType)
        {
            await using var stream = File.OpenRead(sourcePath);
            await fileStorage.UploadAsync(key, stream, contentType);
            return key;
        }

        private static double EstimateSpokenSeconds(string narrative)
        {
            var words = narrative.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return words / 2.5;
        }

        private static async Task SetStepAsync(
            EFDbContext dbContext, UserMemory job, string step, CancellationToken cancellationToken)
        {
            job.ProcessingStep = step;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private void CleanUp(string workingDirectory)
        {
            if (_keepIntermediates)
            {
                _logger.LogInformation("Keeping intermediates at '{WorkingDirectory}'.", workingDirectory);
                return;
            }
            try
            {
                if (Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, recursive: true);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete '{WorkingDirectory}'.", workingDirectory);
            }
        }
    }
}
