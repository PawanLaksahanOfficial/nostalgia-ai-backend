using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MemoriesController : ControllerBase
    {
        private readonly IMemoryRepository _memoryRepository;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IFileStorage _fileStorage;
        private readonly EFDbContext _dbContext;
        public MemoriesController(IMemoryRepository memoryRepository, ISubscriptionService subscriptionService, IFileStorage fileStorage, EFDbContext dbContext)
        {
            _memoryRepository = memoryRepository;
            _subscriptionService = subscriptionService;
            _fileStorage = fileStorage;
            _dbContext = dbContext;
        }

        [HttpGet("my")]
        public async Task<ActionResult> GetMyMemories()
        {
            try
            {
                var userId = GetUserId();
                var memories = await _memoryRepository.GetByUserIdAsync(userId);
                return Ok(memories.Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.Status,
                    m.CreatedAt,
                    m.CompletedAt,
                    m.IsPublic,
                    m.ViewCount,
                    m.ThumbnailPath,
                    HasVideo = !string.IsNullOrEmpty(m.FinalVideoPath)
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult> GetMemory(int id)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(id);
                if (memory == null || memory.UserId != userId)
                {
                    return NotFound("Memory not found.");
                }
                return Ok(new
                {
                    memory.Id,
                    memory.Title,
                    memory.StoryText,
                    memory.GeneratedNarrative,
                    memory.Status,
                    memory.FinalVideoPath,
                    memory.ThumbnailPath,
                    memory.IsPublic,
                    memory.ShareToken,
                    memory.ShareExpiresAt,
                    memory.ViewCount,
                    memory.CreatedAt,
                    memory.CompletedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{id}/share")]
        public async Task<ActionResult> ShareMemory(int id, [FromBody] ShareMemoryRequest request)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(id);
                if (memory == null || memory.UserId != userId)
                {
                    return NotFound("Memory not found.");
                }

                if (memory.Status != VideoStatus.Completed)
                {
                    return BadRequest("Memory is not ready to share.");
                }
                // Generate unique share token
                memory.ShareToken = Guid.NewGuid().ToString("N");
                memory.IsPublic = request.MakePublic;
                if (request.ExpirationDays.HasValue)
                {
                    memory.ShareExpiresAt = DateTime.UtcNow.AddDays(request.ExpirationDays.Value);
                }
                else
                {
                    var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
                    if (quota.MonthlyMemoriesLimit > 3) // Premium tier
                    {
                        memory.ShareExpiresAt = null; // Permanent
                    }
                    else
                    {
                        memory.ShareExpiresAt = DateTime.UtcNow.AddDays(7); // Free tier: 7 days
                    }
                }
                await _memoryRepository.UpdateAsync(memory);
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var shareUrl = $"{baseUrl}/api/memories/share/{memory.ShareToken}";
                return Ok(new MemoryShareResponse
                {
                    MemoryId = memory.Id,
                    ShareToken = memory.ShareToken,
                    ShareUrl = shareUrl,
                    ExpiresAt = memory.ShareExpiresAt,
                    IsPublic = memory.IsPublic
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}/share")]
        public async Task<ActionResult> UnshareMemory(int id)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(id);
                if (memory == null || memory.UserId != userId)
                {
                    return NotFound("Memory not found.");
                }

                memory.IsPublic = false;
                memory.ShareToken = null;
                memory.ShareExpiresAt = null;
                await _memoryRepository.UpdateAsync(memory);

                return Ok(new { message = "Memory unshared successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("share/{token}")]
        [AllowAnonymous]
        public async Task<ActionResult> GetSharedMemory(string token)
        {
            try
            {
                var memory = await _memoryRepository.GetByShareTokenAsync(token);
                if (memory == null)
                {
                    return NotFound("Shared memory not found or no longer available.");
                }
                // Check if link has expired
                if (memory.ShareExpiresAt.HasValue && memory.ShareExpiresAt.Value < DateTime.UtcNow)
                {
                    return BadRequest("This share link has expired.");
                }
                // Increment view count
                await _memoryRepository.IncrementViewCountAsync(memory.Id);
                return Ok(new SharedMemoryResponse
                {
                    Id = memory.Id,
                    Title = memory.Title,
                    StoryText = memory.StoryText,
                    FinalVideoPath = memory.FinalVideoPath,
                    ThumbnailPath = memory.ThumbnailPath,
                    Quality = memory.Quality,
                    ViewCount = memory.ViewCount + 1,
                    AuthorName = $"{memory.User.FirstName} {memory.User.LastName}",
                    CreatedAt = memory.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteMemory(int id)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(id);
                if (memory == null || memory.UserId != userId)
                {
                    return NotFound("Memory not found.");
                }
                // Delete associated files
                if (!string.IsNullOrEmpty(memory.FinalVideoPath))
                {
                    await _fileStorage.DeleteAsync(memory.FinalVideoPath);
                }
                if (!string.IsNullOrEmpty(memory.ThumbnailPath))
                {
                    await _fileStorage.DeleteAsync(memory.ThumbnailPath);
                }
                if (!string.IsNullOrEmpty(memory.UserImagePath))
                {
                    await _fileStorage.DeleteAsync(memory.UserImagePath);
                }

                await _memoryRepository.DeleteAsync(id);
                return Ok(new { message = "Memory deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{id}/regenerate")]
        public async Task<ActionResult> RegenerateMemory(int id, [FromBody] RegenerateMemoryRequest request)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(id);
                if (memory == null || memory.UserId != userId)
                {
                    return NotFound("Memory not found.");
                }
                if (memory.Status == VideoStatus.Processing)
                {
                    return BadRequest("Memory is currently being processed.");
                }
                // Update story text if provided
                if (!string.IsNullOrEmpty(request.NewStoryText))
                {
                    memory.StoryText = request.NewStoryText;
                }
                // Update music mood if provided
                if (!string.IsNullOrEmpty(request.NewMusicMood))
                {
                    memory.MusicMood = request.NewMusicMood;
                }
                // Reset status to pending for reprocessing
                memory.Status = VideoStatus.Pending;
                memory.GeneratedNarrative = null;
                memory.GeneratedMusicPath = null;
                memory.VoiceoverPath = null;
                memory.FinalVideoPath = null;
                memory.CompletedAt = null;
                await _memoryRepository.UpdateAsync(memory);

                // TODO: Trigger background worker to process this memory

                return Ok(new { message = "Memory regeneration started.", memoryId = memory.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{id}/download")]
        public async Task<IActionResult> DownloadVideo(int id)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(id);
                if (memory == null || memory.UserId != userId)
                {
                    return NotFound("Memory not found.");
                }

                if (memory.Status != VideoStatus.Completed || string.IsNullOrEmpty(memory.FinalVideoPath))
                {
                    return BadRequest("Video is not ready for download.");
                }

                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null) return NotFound("User not found.");
                // Check if user has access to this quality
                if (memory.Quality == VideoQuality.HD && user.Tier == UserTier.Free)
                {
                    return BadRequest("HD quality is only available for Premium users.");
                }
                var videoPath = Path.Combine(Directory.GetCurrentDirectory(), "storage", memory.FinalVideoPath);
                if (!System.IO.File.Exists(videoPath))
                {
                    return NotFound("Video file not found.");
                }
                var fileBytes = await System.IO.File.ReadAllBytesAsync(videoPath);
                var fileName = $"{memory.Title.Replace(" ", "_")}.mp4";
                
                return File(fileBytes, "video/mp4", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<ActionResult> GetPublicMemories()
        {
            try
            {
                var memories = await _memoryRepository.GetPublicMemoriesAsync();
                return Ok(memories.Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.ThumbnailPath,
                    m.ViewCount,
                    AuthorName = $"{m.User.FirstName} {m.User.LastName}",
                    m.CreatedAt
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid user token.");
            }
            return userId;
        }
    }
}