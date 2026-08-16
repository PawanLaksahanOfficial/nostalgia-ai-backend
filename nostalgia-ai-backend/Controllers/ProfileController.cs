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
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IMemoryRepository _memoryRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            IUserRepository userRepository,
            ISubscriptionService subscriptionService,
            IMemoryRepository memoryRepository,
            IPasswordHasher passwordHasher,
            ILogger<ProfileController> logger)
        {
            _userRepository = userRepository;
            _subscriptionService = subscriptionService;
            _memoryRepository = memoryRepository;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        [HttpGet("myProfile")]
        public async Task<ActionResult<ApiResponse<object>>> GetMyProfile()
        {
            try
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
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetMyProfile.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpPut("myProfile")]
        public async Task<ActionResult<ApiResponse<object>>> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                if (!string.IsNullOrEmpty(request.AvatarUrl) &&
                    (!Uri.TryCreate(request.AvatarUrl, UriKind.Absolute, out var avatarUri) ||
                     (avatarUri.Scheme != Uri.UriSchemeHttp && avatarUri.Scheme != Uri.UriSchemeHttps)))
                {
                    return BadRequest(ApiResponse<object>.Fail("Avatar URL must be a valid http(s) URL."));
                }

                var userId = GetUserId();
                var result = await _userRepository.UpdateProfileAsync(userId, request);
                if (!result)
                {
                    return NotFound(ApiResponse<object>.NotFound("User not found."));
                }

                return Ok(ApiResponse<object>.Ok(new { }, "Profile updated successfully."));
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in UpdateProfile.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpPut("change-password")]
        public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
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
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ChangePassword.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
            }
        }

        [HttpGet("memories")]
        public async Task<ActionResult<ApiResponse<object>>> GetMyMemories()
        {
            try
            {
                var userId = GetUserId();
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
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid user token."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GetMyMemories.");
                return BadRequest(ApiResponse<object>.Fail("An unexpected error occurred. Please try again."));
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