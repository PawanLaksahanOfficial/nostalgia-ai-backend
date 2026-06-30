using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SocialController : ControllerBase
    {
        private readonly IMemoryRepository _memoryRepository;

    private readonly EFDbContext _dbContext;

    public SocialController(IMemoryRepository memoryRepository, EFDbContext dbContext)
    {
        _memoryRepository = memoryRepository;
        _dbContext = dbContext;
    }

        [HttpPost("memories/{memoryId}/like")]
        public async Task<ActionResult> ToggleLike(int memoryId)
        {
            try
            {
                var userId = GetUserId();
                var isLiked = await _memoryRepository.ToggleLikeAsync(memoryId, userId);
                var likeCount = await _memoryRepository.GetLikeCountAsync(memoryId);
                return Ok(new LikeResponse
                {
                    MemoryId = memoryId,
                    LikeCount = likeCount,
                    IsLikedByCurrentUser = isLiked
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("memories/{memoryId}/likes")]
        public async Task<ActionResult> GetLikeCount(int memoryId)
        {
            try
            {
                var count = await _memoryRepository.GetLikeCountAsync(memoryId);
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("memories/{memoryId}/comments")]
        public async Task<ActionResult> AddComment(int memoryId, [FromBody] CommentRequest request)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(memoryId);
                if (memory == null)
                {
                    return NotFound("Memory not found.");
                }

                var comment = new Comment
                {
                    MemoryId = memoryId,
                    UserId = userId,
                    Text = request.Text,
                    CreatedAt = DateTime.UtcNow
                };

                var createdComment = await _memoryRepository.AddCommentAsync(comment);

                return Ok(new CommentResponse
                {
                    Id = createdComment.Id,
                    UserId = createdComment.UserId,
                    UserName = $"{memory.User.FirstName} {memory.User.LastName}",
                    Text = createdComment.Text,
                    CreatedAt = createdComment.CreatedAt,
                    UpdatedAt = createdComment.UpdatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("memories/{memoryId}/comments")]
        public async Task<ActionResult> GetComments(int memoryId)
        {
            try
            {
                var comments = await _memoryRepository.GetCommentsByMemoryIdAsync(memoryId);
                return Ok(comments.Select(c => new CommentResponse
                {
                    Id = c.Id,
                    UserId = c.UserId,
                    UserName = $"{c.User.FirstName} {c.User.LastName}",
                    Text = c.Text,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("comments/{commentId}")]
        public async Task<ActionResult> DeleteComment(int commentId)
        {
            try
            {
                var userId = GetUserId();
                var result = await _memoryRepository.DeleteCommentAsync(commentId, userId);
                if (!result)
                {
                    return NotFound("Comment not found or you don't have permission to delete it.");
                }

                return Ok(new { message = "Comment deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("collections")]
        public async Task<ActionResult> CreateCollection([FromBody] CreateCollectionRequest request)
        {
            try
            {
                var userId = GetUserId();
                var collection = new Collection
                {
                    UserId = userId,
                    Name = request.Name,
                    Description = request.Description,
                    CreatedAt = DateTime.UtcNow
                };

                var created = await _memoryRepository.CreateCollectionAsync(collection);

                return Ok(new CollectionResponse
                {
                    Id = created.Id,
                    Name = created.Name,
                    Description = created.Description,
                    MemoryCount = 0,
                    CreatedAt = created.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("collections")]
        public async Task<ActionResult> GetMyCollections()
        {
            try
            {
                var userId = GetUserId();
                var collections = await _memoryRepository.GetUserCollectionsAsync(userId);               
                return Ok(collections.Select(c => new CollectionResponse
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    MemoryCount = c.Memories.Count,
                    CreatedAt = c.CreatedAt
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("collections/{collectionId}")]
        public async Task<ActionResult> GetCollection(int collectionId)
        {
            try
            {
                var userId = GetUserId();
                var collection = await _memoryRepository.GetCollectionByIdAsync(collectionId, userId);
                if (collection == null)
                {
                    return NotFound("Collection not found.");
                }
                return Ok(new CollectionResponse
                {
                    Id = collection.Id,
                    Name = collection.Name,
                    Description = collection.Description,
                    MemoryCount = collection.Memories.Count,
                    CreatedAt = collection.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("collections/{collectionId}")]
        public async Task<ActionResult> UpdateCollection(int collectionId, [FromBody] UpdateCollectionRequest request)
        {
            try
            {
                var userId = GetUserId();
                var collection = await _memoryRepository.GetCollectionByIdAsync(collectionId, userId);
                if (collection == null)
                {
                    return NotFound("Collection not found.");
                }
                if (!string.IsNullOrEmpty(request.Name))
                    collection.Name = request.Name;
                if (request.Description != null)
                    collection.Description = request.Description;             
                collection.UpdatedAt = DateTime.UtcNow;
                await _memoryRepository.UpdateCollectionAsync(collection);
                return Ok(new CollectionResponse
                {
                    Id = collection.Id,
                    Name = collection.Name,
                    Description = collection.Description,
                    MemoryCount = collection.Memories.Count,
                    CreatedAt = collection.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("collections/{collectionId}")]
        public async Task<ActionResult> DeleteCollection(int collectionId)
        {
            try
            {
                var userId = GetUserId();
                var result = await _memoryRepository.DeleteCollectionAsync(collectionId, userId);
                if (!result)
                {
                    return NotFound("Collection not found.");
                }
                return Ok(new { message = "Collection deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("collections/{collectionId}/memories")]
        public async Task<ActionResult> AddMemoryToCollection(int collectionId, [FromBody] AddMemoryToCollectionRequest request)
        {
            try
            {
                var userId = GetUserId();
                var collection = await _memoryRepository.GetCollectionByIdAsync(collectionId, userId);
                if (collection == null)
                {
                    return NotFound("Collection not found.");
                }
                var result = await _memoryRepository.AddMemoryToCollectionAsync(collectionId, request.MemoryId);
                if (!result)
                {
                    return BadRequest("Failed to add memory to collection.");
                }
                return Ok(new { message = "Memory added to collection." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("collections/{collectionId}/memories/{memoryId}")]
        public async Task<ActionResult> RemoveMemoryFromCollection(int collectionId, int memoryId)
        {
            try
            {
                var userId = GetUserId();
                var collection = await _memoryRepository.GetCollectionByIdAsync(collectionId, userId);
                if (collection == null)
                {
                    return NotFound("Collection not found.");
                }
                var result = await _memoryRepository.RemoveMemoryFromCollectionAsync(collectionId, memoryId);
                if (!result)
                {
                    return BadRequest("Failed to remove memory from collection.");
                }
                return Ok(new { message = "Memory removed from collection." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("collections/{collectionId}/memories")]
        public async Task<ActionResult> GetMemoriesInCollection(int collectionId)
        {
            try
            {
                var userId = GetUserId();
                var collection = await _memoryRepository.GetCollectionByIdAsync(collectionId, userId);
                if (collection == null)
                {
                    return NotFound("Collection not found.");
                }
                var memories = await _memoryRepository.GetMemoriesInCollectionAsync(collectionId);
                return Ok(memories.Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.Status,
                    m.CreatedAt,
                    m.CompletedAt,
                    m.ThumbnailPath,
                    HasVideo = !string.IsNullOrEmpty(m.FinalVideoPath)
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("gifts")]
        public async Task<ActionResult> SendGift([FromBody] GiftMemoryRequest request)
        {
            try
            {
                var userId = GetUserId();
                var memory = await _memoryRepository.GetByIdAsync(request.MemoryId);
                if (memory == null)
                {
                    return NotFound("Memory not found.");
                }
                var gift = new Gift
                {
                    SenderId = userId,
                    MemoryId = request.MemoryId,
                    RecipientEmail = request.RecipientEmail,
                    Message = request.Message,
                    CreatedAt = DateTime.UtcNow
                };
                var created = await _memoryRepository.CreateGiftAsync(gift);
                return Ok(new GiftResponse
                {
                    Id = created.Id,
                    MemoryId = created.MemoryId,
                    MemoryTitle = memory.Title,
                    SenderName = $"{memory.User.FirstName} {memory.User.LastName}",
                    RecipientEmail = created.RecipientEmail,
                    Message = created.Message,
                    IsOpened = created.IsOpened,
                    CreatedAt = created.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("gifts/sent")]
        public async Task<ActionResult> GetSentGifts()
        {
            try
            {
                var userId = GetUserId();
                var gifts = await _memoryRepository.GetSentGiftsAsync(userId);
                return Ok(gifts.Select(g => new GiftResponse
                {
                    Id = g.Id,
                    MemoryId = g.MemoryId,
                    MemoryTitle = g.Memory.Title,
                    SenderName = $"{g.Sender.FirstName} {g.Sender.LastName}",
                    RecipientEmail = g.RecipientEmail,
                    Message = g.Message,
                    IsOpened = g.IsOpened,
                    CreatedAt = g.CreatedAt
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("gifts/received")]
        public async Task<ActionResult> GetReceivedGifts()
        {
            try
            {
                var user = await _dbContext.Users.FindAsync(GetUserId());
                if (user == null)
                {
                    return NotFound("User not found.");
                }
                var gifts = await _memoryRepository.GetReceivedGiftsAsync(user.Email);
                return Ok(gifts.Select(g => new GiftResponse
                {
                    Id = g.Id,
                    MemoryId = g.MemoryId,
                    MemoryTitle = g.Memory.Title,
                    SenderName = $"{g.Sender.FirstName} {g.Sender.LastName}",
                    RecipientEmail = g.RecipientEmail,
                    Message = g.Message,
                    IsOpened = g.IsOpened,
                    CreatedAt = g.CreatedAt
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("gifts/{giftId}")]
        public async Task<ActionResult> GetGift(int giftId)
        {
            try
            {
                var gift = await _memoryRepository.GetGiftByIdAsync(giftId);
                if (gift == null)
                {
                    return NotFound("Gift not found.");
                }
                return Ok(new GiftResponse
                {
                    Id = gift.Id,
                    MemoryId = gift.MemoryId,
                    MemoryTitle = gift.Memory.Title,
                    SenderName = $"{gift.Sender.FirstName} {gift.Sender.LastName}",
                    RecipientEmail = gift.RecipientEmail,
                    Message = gift.Message,
                    IsOpened = gift.IsOpened,
                    CreatedAt = gift.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("gifts/{giftId}/open")]
        public async Task<ActionResult> OpenGift(int giftId)
        {
            try
            {
                var result = await _memoryRepository.MarkGiftAsOpenedAsync(giftId);
                if (!result)
                {
                    return NotFound("Gift not found.");
                }
                return Ok(new { message = "Gift opened!" });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("prompts/daily")]
        public async Task<ActionResult> GetDailyPrompt()
        {
            try
            {
                var prompts = new[]
                {
                    "What's a smell that instantly takes you back to your childhood?",
                    "Describe your favorite holiday memory from when you were young.",
                    "What's a song that always makes you nostalgic?",
                    "Tell me about your best friend growing up.",
                    "What was your favorite place to visit as a child?",
                    "Describe a summer day from your childhood.",
                    "What's a family tradition you'll never forget?",
                    "Tell me about your first day of school.",
                    "What was your favorite meal as a kid?",
                    "Describe a place that holds special memories for you."
                };
                var random = new Random();
                var today = DateTime.UtcNow.Date;
                var dayOfYear = today.DayOfYear;
                var promptIndex = dayOfYear % prompts.Length;
                return Ok(new
                {
                    prompt = prompts[promptIndex],
                    date = today.ToString("MMMM dd, yyyy")
                });
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