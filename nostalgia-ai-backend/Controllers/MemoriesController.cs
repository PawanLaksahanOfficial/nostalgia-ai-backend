using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MemoriesController : ControllerBase
    {
        private const string QuotaExceededMessage =
            "Monthly memory limit reached. Upgrade to Premium for a higher limit.";

        private readonly IAIService _aIService;
        private readonly IMemoryRepository _memoryRepository;
        private readonly ISubscriptionService _subscriptionService;

        public MemoriesController(
            IAIService aIService,
            IMemoryRepository memoryRepository,
            ISubscriptionService subscriptionService)
        {
            _aIService = aIService;
            _memoryRepository = memoryRepository;
            _subscriptionService = subscriptionService;
        }

        [HttpPost("generate")]
        [Authorize]
        [EnableRateLimiting("ai-generation")]
        public async Task<ActionResult<ApiResponse<string>>> GenerateNostalgicFeeling([FromBody] GenerateRequest request)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(ApiResponse<string>.Fail("Invalid user token."));
            }
            if (!await _subscriptionService.TryConsumeQuotaAsync(userId))
            {
                return StatusCode(403, ApiResponse<string>.Fail(QuotaExceededMessage));
            }

            try
            {
                var result = await _aIService.GenerateNostalgicTextAsync(request.Text);
                return Ok(ApiResponse<string>.Ok(result));
            }
            catch
            {
                await _subscriptionService.RefundQuotaAsync(userId);
                throw;
            }
        }

        [HttpPost("create")]
        [Authorize]
        [EnableRateLimiting("ai-generation")]
        public async Task<ActionResult<ApiResponse<object>>> CreateMemoryVideo([FromBody] CreateMemoryRequest request)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
            }

            if (!await _subscriptionService.TryConsumeQuotaAsync(userId))
            {
                return StatusCode(403, ApiResponse<object>.Fail(QuotaExceededMessage));
            }

            try
            {
                var memory = new UserMemory
                {
                    UserId = userId,
                    Title = request.Title,
                    StoryText = request.StoryText,
                    MusicMood = request.MusicMood,
                    Quality = VideoQuality.Standard,
                    Status = VideoStatus.Pending,
                    IsPublic = false,
                    CreatedAt = DateTime.UtcNow
                };

                var id = await _memoryRepository.CreateAsync(memory);
                if (id <= 0)
                {
                    await _subscriptionService.RefundQuotaAsync(userId);
                    return BadRequest(ApiResponse<object>.Fail("Failed to create memory."));
                }

                return Ok(ApiResponse<object>.Ok(new { jobId = id, status = "pending" }, "Memory creation queued."));
            }
            catch
            {
                await _subscriptionService.RefundQuotaAsync(userId);
                throw;
            }
        }

        [HttpGet("status/{id}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> GetMemoryStatus(int id)
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
            }

            var memory = await _memoryRepository.GetByIdForUserAsync(id, userId);
            if (memory == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Memory not found."));
            }

            var result = new
            {
                memory.Id,
                memory.Title,
                memory.Status,
                memory.GeneratedNarrative,
                VideoUrl = memory.FinalVideoPath,
                memory.CreatedAt,
                memory.CompletedAt
            };

            return Ok(ApiResponse<object>.Ok(result));
        }

        [HttpGet("my")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> GetMyMemories()
        {
            if (!TryGetUserId(out var userId))
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
            }

            var memories = await _memoryRepository.GetByUserIdAsync(userId);
            var result = memories.Select(m => new
            {
                m.Id,
                m.Title,
                m.Status,
                m.CreatedAt,
                m.CompletedAt,
                HasVideo = !string.IsNullOrEmpty(m.FinalVideoPath)
            });

            return Ok(ApiResponse<object>.Ok(result));
        }

        private bool TryGetUserId(out int userId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out userId);
        }
    }

    public class CreateMemoryRequest
    {
        public string Title { get; set; } = string.Empty;
        public string StoryText { get; set; } = string.Empty;
        public string? MusicMood { get; set; }
    }
}