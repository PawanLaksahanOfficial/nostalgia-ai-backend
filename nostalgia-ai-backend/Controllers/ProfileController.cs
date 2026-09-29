using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private const long MaxAvatarBytes = 2 * 1024 * 1024;
        private const long MaxAvatarRequestBytes = 3_000_000;
        private const string AvatarRoutePrefix = "/api/profile/avatar/";

        private static readonly Regex AvatarFileNamePattern =
            new("^[a-f0-9]{32}\\.(jpg|png|webp)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly IUserRepository _userRepository;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IMemoryRepository _memoryRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IFileStorage _fileStorage;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            IUserRepository userRepository,
            ISubscriptionService subscriptionService,
            IMemoryRepository memoryRepository,
            IPasswordHasher passwordHasher,
            IFileStorage fileStorage,
            ILogger<ProfileController> logger)
        {
            _userRepository = userRepository;
            _subscriptionService = subscriptionService;
            _memoryRepository = memoryRepository;
            _passwordHasher = passwordHasher;
            _fileStorage = fileStorage;
            _logger = logger;
        }

        [HttpPost("avatar")]
        [RequestSizeLimit(MaxAvatarRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAvatarRequestBytes)]
        public async Task<ActionResult<ApiResponse<object>>> UploadAvatar(IFormFile? file)
        {
            var userId = GetUserId();
            if (file is not { Length: > 0 })
            {
                return BadRequest(ApiResponse<object>.Fail("Please choose an image to upload."));
            }
            if (file.Length > MaxAvatarBytes)
            {
                return BadRequest(ApiResponse<object>.Fail("Profile photos must be 2MB or smaller."));
            }
            await using var content = file.OpenReadStream();
            if (!ImageValidator.TryResolve(content, out var contentType, out var extension))
            {
                return BadRequest(ApiResponse<object>.Fail("Please upload a JPEG, PNG, or WebP image."));
            }
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return NotFound(ApiResponse<object>.NotFound("User not found."));
            }
            var previousAvatarUrl = user.AvatarUrl;
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await _fileStorage.UploadAsync(AvatarKey(userId, fileName), content, contentType);
            var avatarUrl = $"{AvatarRoutePrefix}{userId}/{fileName}";
            if (!await _userRepository.SetAvatarUrlAsync(userId, avatarUrl))
            {
                await DeleteStoredAvatarAsync(userId, avatarUrl);
                return BadRequest(ApiResponse<object>.Fail("Failed to update your profile photo."));
            }
            await DeleteStoredAvatarAsync(userId, previousAvatarUrl);
            return Ok(ApiResponse<object>.Ok(new { avatarUrl }, "Profile photo updated."));
        }

        [HttpDelete("avatar")]
        public async Task<ActionResult<ApiResponse<object>>> RemoveAvatar()
        {
            var userId = GetUserId();
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return NotFound(ApiResponse<object>.NotFound("User not found."));
            }
            var previousAvatarUrl = user.AvatarUrl;
            if (!await _userRepository.SetAvatarUrlAsync(userId, null))
            {
                return BadRequest(ApiResponse<object>.Fail("Failed to remove your profile photo."));
            }
            await DeleteStoredAvatarAsync(userId, previousAvatarUrl);
            return Ok(ApiResponse<object>.Ok(new { avatarUrl = (string?)null }, "Profile photo removed."));
        }

        [HttpGet("avatar/{userId:int}/{fileName}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAvatar(int userId, string fileName)
        {
            if (!AvatarFileNamePattern.IsMatch(fileName))
            {
                return NotFound(ApiResponse<object>.NotFound("Photo not found."));
            }
            var stream = await _fileStorage.DownloadAsync(AvatarKey(userId, fileName));
            if (stream == null)
            {
                return NotFound(ApiResponse<object>.NotFound("Photo not found."));
            }
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            var contentType = Path.GetExtension(fileName) switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            return File(stream, contentType);
        }

        [HttpGet("myProfile")]
        public async Task<ActionResult<ApiResponse<object>>> GetMyProfile()
        {
            var userId = GetUserId();
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return NotFound(ApiResponse<object>.NotFound("User not found."));
            }
            var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
            var profile = new
            {
                user.UserId,
                user.FirstName,
                user.LastName,
                user.Email,
                user.AvatarUrl,
                Tier = user.Tier.ToString().ToLower(),
                Quota = quota
            };

            return Ok(ApiResponse<object>.Ok(profile));
        }

        [HttpPut("myProfile")]
        public async Task<ActionResult<ApiResponse<object>>> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = GetUserId();
            var result = await _userRepository.UpdateProfileAsync(userId, request);
            if (!result)
            {
                return NotFound(ApiResponse<object>.NotFound("User not found."));
            }

            return Ok(ApiResponse<object>.Ok(new { }, "Profile updated successfully."));
        }

        [HttpPut("change-password")]
        public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userId = GetUserId();
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.PasswordHash))
            {
                return BadRequest(ApiResponse<object>.Fail("Password authentication not set up for this account."));
            }

            if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(ApiResponse<object>.Fail("Current password is incorrect."));
            }

            var newHash = _passwordHasher.Hash(request.NewPassword);
            var passwordUpdated = await _userRepository.UpdatePasswordAsync(userId, newHash);
            if (!passwordUpdated)
            {
                return BadRequest(ApiResponse<object>.Fail("Failed to update password."));
            }

            return Ok(ApiResponse<object>.Ok(new { }, "Password changed successfully."));
        }

        [HttpGet("memories")]
        public async Task<ActionResult<ApiResponse<object>>> GetMyMemories()
        {
            var userId = GetUserId();
            var memories = await _memoryRepository.GetByUserIdAsync(userId);
            var result = memories.Select(m => new
            {
                m.Id,
                m.Title,
                Status = m.Status.ToString(),
                m.CreatedAt,
                m.CompletedAt,
                HasVideo = !string.IsNullOrEmpty(m.FinalVideoPath)
            });

            return Ok(ApiResponse<object>.Ok(result));
        }

        private static string AvatarKey(int userId, string fileName) => $"avatars/{userId}/{fileName}";

        private async Task DeleteStoredAvatarAsync(int userId, string? avatarUrl)
        {
            var ownPrefix = $"{AvatarRoutePrefix}{userId}/";
            if (string.IsNullOrEmpty(avatarUrl) || !avatarUrl.StartsWith(ownPrefix, StringComparison.Ordinal))
            {
                return;
            }
            var fileName = avatarUrl[ownPrefix.Length..];
            if (!AvatarFileNamePattern.IsMatch(fileName))
            {
                return;
            }
            try
            {
                await _fileStorage.DeleteAsync(AvatarKey(userId, fileName));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete old avatar '{FileName}' for user {UserId}.", fileName, userId);
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