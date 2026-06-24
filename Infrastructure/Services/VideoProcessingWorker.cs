using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class VideoProcessingWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VideoProcessingWorker> _logger;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

        public VideoProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<VideoProcessingWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Video Processing Worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingJobsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing video jobs.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }

        private async Task ProcessPendingJobsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EFDbContext>();
            var aiService = scope.ServiceProvider.GetRequiredService<IAIService>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

            var pendingJobs = await dbContext.UserMemories
                .Where(m => m.Status == VideoStatus.Pending)
                .Take(5)
                .ToListAsync(cancellationToken);

            foreach (var job in pendingJobs)
            {
                try
                {
                    await ProcessJobAsync(job, dbContext, aiService, fileStorage, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process job {JobId}", job.Id);
                    job.Status = VideoStatus.Failed;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }

        private async Task ProcessJobAsync(
            UserMemory job, 
            EFDbContext dbContext, 
            IAIService aiService, 
            IFileStorage fileStorage,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Processing job {JobId}: {Title}", job.Id, job.Title);
            job.Status = VideoStatus.Processing;
            await dbContext.SaveChangesAsync(cancellationToken);

            // Step 1: Generate nostalgic narrative from user's story text
            var narrative = await aiService.GenerateNostalgicTextAsync(job.StoryText);
            job.GeneratedNarrative = narrative;

            // Step 2: Generate music (placeholder - will be wired up in Phase 1)
            // Will use MusicGen or Hugging Face API in Phase 1

            // Step 3: Generate voiceover (placeholder - will be wired up in Phase 1)
            // Will use Edge-TTS in Phase 1

            // Step 4: Assemble video (placeholder - will be wired up in Phase 1)
            // Will use FFmpeg in Phase 1

            // Mark as completed
            job.Status = VideoStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Job {JobId} completed successfully.", job.Id);
        }
    }
}