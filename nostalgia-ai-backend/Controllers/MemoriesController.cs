using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MemoriesController : ControllerBase
    {
        private readonly IAIService _aIService;
        private readonly IMemoryRepository _memoryRepository;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<MemoriesController> _logger;

        public MemoriesController(
            IAIService aIService,
            IMemoryRepository memoryRepository,
            ISubscriptionService subscriptionService,
            ILogger<MemoriesController> logger)
        {
            _aIService = aIService;
            _memoryRepository = memoryRepository;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        [HttpPost("generate")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<string>>> GenerateNostalgicFeeling([FromBody] string userPrompt)
        {
            try
            {
                var result = await _aIService.GenerateNostalgicTextAsync(userPrompt);
                return Ok(ApiResponse<string>.Ok(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GenerateNostalgicFeeling.");
                return BadRequest(ApiResponse<string>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpPost("create")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> CreateMemoryVideo([FromBody] CreateMemoryRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
                }

                var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
                if (quota.MonthlyMemoriesUsed >= quota.MonthlyMemoriesLimit)
                {
                    return StatusCode(403, ApiResponse<object>.Fail("Monthly memory limit reached. Upgrade to Premium for a higher limit."));
                }

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
                    return BadRequest(ApiResponse<object>.Fail("Failed to create memory."));
                }

                await _subscriptionService.IncrementMonthlyUsageAsync(userId);

                return Ok(ApiResponse<object>.Ok(new { jobId = id, status = "pending" }, "Memory creation queued."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in CreateMemoryVideo.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpGet("status/{id}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> GetMemoryStatus(int id)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetMemoryStatus.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpGet("my")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> GetMyMemories()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetMyMemories.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
            }
        }
    }

    public class CreateMemoryRequest
    {
        public string Title { get; set; } = string.Empty;
        public string StoryText { get; set; } = string.Empty;
        public string? MusicMood { get; set; }
    }
}