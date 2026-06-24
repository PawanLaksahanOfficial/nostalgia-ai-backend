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

        public MemoriesController(IAIService aIService, IMemoryRepository memoryRepository)
        {
            _aIService = aIService;
            _memoryRepository = memoryRepository;
        }

        [HttpPost("generate")]
        public async Task<ActionResult<string>> GenerateNostalgicFeeling([FromBody] string userPrompt)
        {
            try
            {
                var result = await _aIService.GenerateNostalgicTextAsync(userPrompt);
                return Ok(result);
            }
            catch (Exception ex) 
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("create")]
        [Authorize]
        public async Task<ActionResult<int>> CreateMemoryVideo([FromBody] CreateMemoryRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized("Invalid user token.");
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
                return Ok(new { jobId = id, status = "pending" });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("status/{id}")]
        [Authorize]
        public async Task<ActionResult> GetMemoryStatus(int id)
        {
            try
            {
                var memory = await _memoryRepository.GetByIdAsync(id);
                if (memory == null)
                {
                    return NotFound("Memory not found.");
                }

                return Ok(new
                {
                    memory.Id,
                    memory.Title,
                    memory.Status,
                    memory.GeneratedNarrative,
                    videoUrl = memory.FinalVideoPath,
                    memory.CreatedAt,
                    memory.CompletedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("my")]
        [Authorize]
        public async Task<ActionResult> GetMyMemories()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized("Invalid user token.");
                }

                var memories = await _memoryRepository.GetByUserIdAsync(userId);
                return Ok(memories.Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.Status,
                    m.CreatedAt,
                    m.CompletedAt,
                    hasVideo = !string.IsNullOrEmpty(m.FinalVideoPath)
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
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