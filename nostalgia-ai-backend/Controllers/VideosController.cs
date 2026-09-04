using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VideosController : ControllerBase
    {
        private const long MaxUploadBytes = 12_000_000;
        private readonly IMemoryRepository _memoryRepository;
        private readonly IShareLinkService _shareLinkService;
        private readonly IFileStorage _fileStorage;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VideosController> _logger;

        public VideosController(
            IMemoryRepository memoryRepository,
            IShareLinkService shareLinkService,
            IFileStorage fileStorage,
            ISubscriptionService subscriptionService,
            IConfiguration configuration,
            ILogger<VideosController> logger)
        {
            _memoryRepository = memoryRepository;
            _shareLinkService = shareLinkService;
            _fileStorage = fileStorage;
            _subscriptionService = subscriptionService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        [EnableRateLimiting("ai-generation")]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
        public async Task<ActionResult<ApiResponse<object>>> CreateVideo([FromForm] CreateVideoRequest request, IFormFile? image)
        {
            var userId = GetUserId();
            var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
            var maxImageBytes = _configuration.GetSection("Video").GetValue("MaxImageBytes", 10_485_760L);
            string? contentType = null;
            string? extension = null;

            if (image is { Length: > 0 })
            {
                if (image.Length > maxImageBytes)
                {
                    return BadRequest(ApiResponse<object>.Fail(
                        $"Images must be {maxImageBytes / 1_048_576}MB or smaller."));
                }
                await using var probe = image.OpenReadStream();
                if (!ImageValidator.TryResolve(probe, out var resolvedType, out var resolvedExtension))
                {
                    return BadRequest(ApiResponse<object>.Fail(
                        "Please upload a JPEG, PNG, or WebP image."));
                }
                contentType = resolvedType;
                extension = resolvedExtension;
            }

            if (!await _subscriptionService.TryConsumeQuotaAsync(userId))
            {
                return StatusCode(403, ApiResponse<object>.Fail(
                    "Monthly memory limit reached. Upgrade to Premium for a higher limit."));
            }

            try
            {
                var memory = new UserMemory
                {
                    UserId = userId,
                    Title = request.Title,
                    StoryText = request.StoryText,
                    MusicMood = request.MusicMood,
                    Quality = string.Equals(quota.Quality, "hd", StringComparison.OrdinalIgnoreCase)
                        ? VideoQuality.HD
                        : VideoQuality.Standard,
                    Status = VideoStatus.Pending,
                    IsPublic = false,
                    CreatedAt = DateTime.UtcNow
                };

                var id = await _memoryRepository.CreateAsync(memory);
                if (id <= 0)
                {
                    await _subscriptionService.RefundQuotaAsync(userId);
                    return BadRequest(ApiResponse<object>.Fail("Failed to create the video."));
                }
                if (image is { Length: > 0 } && extension != null)
                {
                    var key = $"memories/{id}/source{extension}";

                    await using var content = image.OpenReadStream();
                    await _fileStorage.UploadAsync(key, content, contentType!);

                    memory.UserImagePath = key;
                    await _memoryRepository.UpdateAsync(memory);
                }
                return Ok(ApiResponse<object>.Ok(
                    new { id, status = VideoStatus.Pending.ToString() }, "Your video is being created."));
            }
            catch
            {
                await _subscriptionService.RefundQuotaAsync(userId);
                throw;
            }
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<object>>> GetMyVideos()
        {
            var userId = GetUserId();
            var memories = await _memoryRepository.GetByUserIdAsync(userId);
            return Ok(ApiResponse<object>.Ok(memories.Select(ToListItem)));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ApiResponse<object>>> GetVideo(int id)
        {
            var memory = await _memoryRepository.GetByIdForUserAsync(id, GetUserId());
            if (memory == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Video not found."));
            }
            return Ok(ApiResponse<object>.Ok(ToDetail(memory)));
        }

        [HttpGet("{id:int}/status")]
        public async Task<ActionResult<ApiResponse<object>>> GetVideoStatus(int id)
        {
            var memory = await _memoryRepository.GetByIdForUserAsync(id, GetUserId());
            if (memory == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Video not found."));
            }
            return Ok(ApiResponse<object>.Ok(new
            {
                memory.Id,
                Status = memory.Status.ToString(),
                memory.ProcessingStep,
                memory.FailureReason,
                HasVideo = !string.IsNullOrEmpty(memory.FinalVideoPath),
                memory.DurationSeconds,
                memory.CompletedAt
            }));
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<ApiResponse<object>>> RenameVideo(int id, [FromBody] UpdateVideoRequest request)
        {
            var memory = await _memoryRepository.GetByIdForUserAsync(id, GetUserId());
            if (memory == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Video not found."));
            }
            memory.Title = request.Title.Trim();
            await _memoryRepository.UpdateAsync(memory);
            return Ok(ApiResponse<object>.Ok(new { }, "Video renamed."));
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<object>>> DeleteVideo(int id)
        {
            var memory = await _memoryRepository.GetByIdForUserAsync(id, GetUserId());
            if (memory == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Video not found."));
            }
            if (memory.Status == VideoStatus.Processing)
            {
                return BadRequest(ApiResponse<object>.Fail(
                    "This video is still being created. Please try again in a moment."));
            }
            foreach (var key in new[]
                     {
                         memory.UserImagePath,
                         memory.VoiceoverPath,
                         memory.GeneratedMusicPath,
                         memory.FinalVideoPath,
                         memory.ThumbnailPath,
                         memory.CaptionsPath
                     })
            {
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                try
                {
                    await _fileStorage.DeleteAsync(key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete '{Key}' for video {VideoId}.", key, id);
                }
            }
            await _memoryRepository.DeleteAsync(id);
            return Ok(ApiResponse<object>.Ok(new { }, "Video deleted."));
        }

        [HttpGet("{id:int}/stream")]
        [DisableRateLimiting]
        public async Task<IActionResult> StreamVideo(int id) =>
            await ServeAsync(id, thumbnail: false, asAttachment: false);

        [HttpGet("{id:int}/download")]
        [DisableRateLimiting]
        public async Task<IActionResult> DownloadVideo(int id) =>
            await ServeAsync(id, thumbnail: false, asAttachment: true);

        [HttpGet("{id:int}/thumbnail")]
        [DisableRateLimiting]
        public async Task<IActionResult> GetThumbnail(int id) =>
            await ServeAsync(id, thumbnail: true, asAttachment: false);

        [HttpPost("{id:int}/share")]
        public async Task<ActionResult<ApiResponse<ShareLinkDto>>> CreateShareLink(
            int id, [FromBody] CreateShareLinkRequest request)
        {
            var userId = GetUserId();
            if (await _memoryRepository.GetByIdForUserAsync(id, userId) == null)
            {
                return NotFound(ApiResponse<ShareLinkDto>.NotFound("Video not found."));
            }
            try
            {
                var shareLink = await _shareLinkService.CreateAsync(
                    id, userId, request.ExpiresInDays, request.Label);

                return Ok(ApiResponse<ShareLinkDto>.Ok(shareLink, "Share link created."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<ShareLinkDto>.Fail(ex.Message));
            }
        }

        [HttpGet("{id:int}/share")]
        public async Task<ActionResult<ApiResponse<IEnumerable<ShareLinkDto>>>> GetShareLinks(int id)
        {
            var userId = GetUserId();
            if (await _memoryRepository.GetByIdForUserAsync(id, userId) == null)
            {
                return NotFound(ApiResponse<IEnumerable<ShareLinkDto>>.NotFound("Video not found."));
            }
            var links = await _shareLinkService.GetForMemoryAsync(id, userId);
            return Ok(ApiResponse<IEnumerable<ShareLinkDto>>.Ok(links));
        }

        [HttpDelete("{id:int}/share/{shareId:int}")]
        public async Task<ActionResult<ApiResponse<object>>> RevokeShareLink(int id, int shareId)
        {
            var userId = GetUserId();
            if (await _memoryRepository.GetByIdForUserAsync(id, userId) == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Video not found."));
            }
            try
            {
                await _shareLinkService.RevokeAsync(shareId, userId);
                return Ok(ApiResponse<object>.Ok(new { }, "Share link revoked."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        private async Task<IActionResult> ServeAsync(int id, bool thumbnail, bool asAttachment)
        {
            var memory = await _memoryRepository.GetByIdForUserAsync(id, GetUserId());
            if (memory == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Video not found."));
            }
            var key = thumbnail ? memory.ThumbnailPath : memory.FinalVideoPath;
            if (string.IsNullOrEmpty(key))
            {
                return NotFound(ApiResponse<object>.NotFound("This video has no file yet."));
            }
            var stream = await _fileStorage.DownloadAsync(key);
            if (stream == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Video file is missing."));
            }
            var contentType = thumbnail ? "image/jpeg" : memory.ContentType ?? "video/mp4";
            if (!asAttachment)
            {
                return File(stream, contentType, enableRangeProcessing: true);
            }
            var fileName = $"{FileNameSlug.Create(memory.Title)}{(thumbnail ? ".jpg" : ".mp4")}";
            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }

        private static object ToListItem(UserMemory memory) => new
        {
            memory.Id,
            memory.Title,
            Status = memory.Status.ToString(),
            Quality = memory.Quality.ToString(),
            memory.MusicMood,
            memory.DurationSeconds,
            memory.FileSizeBytes,
            memory.IsPublic,
            memory.ViewCount,
            memory.ProcessingStep,
            memory.FailureReason,
            HasVideo = !string.IsNullOrEmpty(memory.FinalVideoPath),
            HasThumbnail = !string.IsNullOrEmpty(memory.ThumbnailPath),
            memory.CreatedAt,
            memory.CompletedAt
        };

        private static object ToDetail(UserMemory memory) => new
        {
            memory.Id,
            memory.Title,
            Status = memory.Status.ToString(),
            Quality = memory.Quality.ToString(),
            memory.MusicMood,
            memory.DurationSeconds,
            memory.FileSizeBytes,
            memory.IsPublic,
            memory.ViewCount,
            memory.ProcessingStep,
            memory.FailureReason,
            HasVideo = !string.IsNullOrEmpty(memory.FinalVideoPath),
            HasThumbnail = !string.IsNullOrEmpty(memory.ThumbnailPath),
            memory.CreatedAt,
            memory.CompletedAt,
            memory.StoryText,
            Narrative = memory.GeneratedNarrative
        };

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
