using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace Infrastructure.Services
{
    public class VideoProcessingService
    {
        private readonly EFDbContext _dbContext;
        private readonly IFileStorage _fileStorage;
        private readonly IConfiguration _configuration;
        private readonly string _storagePath;

        public VideoProcessingService(EFDbContext dbContext, IFileStorage fileStorage, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _fileStorage = fileStorage;
            _configuration = configuration;
            _storagePath = _configuration["Storage:LocalPath"] ?? "storage";
        }

        public async Task GenerateThumbnailAsync(int memoryId)
        {
            var memory = await _dbContext.UserMemories.FindAsync(memoryId);
            if (memory == null || string.IsNullOrEmpty(memory.FinalVideoPath)) return;
            try
            {
                var videoPath = Path.Combine(Directory.GetCurrentDirectory(), _storagePath, memory.FinalVideoPath);
                var thumbnailPath = Path.Combine("thumbnails", $"{memoryId}.jpg");
                var thumbnailFullPath = Path.Combine(Directory.GetCurrentDirectory(), _storagePath, thumbnailPath);
                Directory.CreateDirectory(Path.GetDirectoryName(thumbnailFullPath)!);
                // FFmpeg command to extract frame at 2 seconds
                var ffmpegArgs = $"-i \"{videoPath}\" -ss 00:00:02 -vframes 1 -q:v 2 \"{thumbnailFullPath}\"";
                var processInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        memory.ThumbnailPath = thumbnailPath;
                        await _dbContext.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Thumbnail generation failed: {ex.Message}");
            }
        }

        public async Task<string?> GetVideoPathAsync(int memoryId, UserTier userTier)
        {
            var memory = await _dbContext.UserMemories.FindAsync(memoryId);
            if (memory == null || string.IsNullOrEmpty(memory.FinalVideoPath)) return null;
            var videoPath = Path.Combine(Directory.GetCurrentDirectory(), _storagePath, memory.FinalVideoPath);           
            // For premium users, return original video
            // For free users, apply watermark here (future enhancement)
            if (userTier == UserTier.Free && memory.Quality == VideoQuality.HD)
            {
                // Free users should only get standard quality
                // In a real implementation, you'd have separate quality files
                // For now, just return the existing file
            }
            return videoPath;
        }
    }
}