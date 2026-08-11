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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Host shutdown requested during job processing
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error occurred while processing video jobs.");
                }

                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when host stops during polling delay
                    break;
                }
            }

            _logger.LogInformation("Video Processing Worker stopped.");
        }

        private async Task ProcessPendingJobsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EFDbContext>();
            var aiService = scope.ServiceProvider.GetRequiredService<IAIService>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

            // Fix 1: Add OrderBy to ensure deterministic polling order
            var pendingJobs = await dbContext.UserMemories
                .Where(m => m.Status == VideoStatus.Pending)
                .OrderBy(m => m.CreatedAt) // Or m.Id
                .Take(5)
                .ToListAsync(cancellationToken);

            if (!pendingJobs.Any())
                return;

            // Fix 3: Claim batch immediately to prevent duplicate processing
            foreach (var job in pendingJobs)
            {
                job.Status = VideoStatus.Processing;
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var job in pendingJobs)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessJobAsync(job, dbContext, aiService, fileStorage, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Re-throw to exit worker loop cleanly without failing job status permanently
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process job {JobId}", job.Id);
                    job.Status = VideoStatus.Failed;

                    // Ensured save on failure even if cancellation was requested mid-execution
                    await dbContext.SaveChangesAsync(CancellationToken.None);
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

            // Step 1: Generate narrative passing cancellation token
            var narrative = await aiService.GenerateNostalgicTextAsync(job.StoryText, cancellationToken);
            job.GeneratedNarrative = narrative;

            // Step 2: Music generation (Phase 1)
            // Step 3: Voiceover generation (Phase 1)
            // Step 4: Video assembly (Phase 1)

            job.Status = VideoStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Job {JobId} completed successfully.", job.Id);
        }
    }
}