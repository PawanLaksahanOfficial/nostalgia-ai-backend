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
    public class ProfileController : ControllerBase
    {
        private readonly EFDbContext _dbContext;
        private readonly IUserRepository _userRepository;
        private readonly ISubscriptionService _subscriptionService;

        public ProfileController(EFDbContext dbContext, IUserRepository userRepository, ISubscriptionService subscriptionService)
        {
            _dbContext = dbContext;
            _userRepository = userRepository;
            _subscriptionService = subscriptionService;
        }

        [HttpGet("me")]
        public async Task<ActionResult> GetMyProfile()
        {
            try
            {
                var userId = GetUserId();
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null) 
                {
                    return NotFound("User not found.");
                }
                var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
                return Ok(new
                {
                    user.UserId,
                    user.FirstName,
                    user.LastName,
                    user.Email,
                    user.AvatarUrl,
                    Tier = user.Tier.ToString().ToLower(),
                    Quota = quota
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("me")]
        public async Task<ActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                var userId = GetUserId();
                var result = await _userRepository.UpdateProfileAsync(userId, request);
                if (!result) 
                {
                    return NotFound("User not found.");
                }
                return Ok(new { message = "Profile updated successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("change-password")]
        public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                var userId = GetUserId();
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    return BadRequest("Password authentication not set up for this account.");
                }
                var passwordHasher = new Infrastructure.Services.PasswordHasherService();
                if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
                {
                    return BadRequest("Current password is incorrect.");
                }
                var newHash = passwordHasher.Hash(request.NewPassword);
                await _userRepository.UpdatePasswordAsync(userId, newHash);
                return Ok(new { message = "Password changed successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("memories")]
        public async Task<ActionResult> GetMyMemories()
        {
            try
            {
                var userId = GetUserId();
                var memories = await _dbContext.UserMemories
                    .Where(m => m.UserId == userId)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => new
                    {
                        m.Id,
                        m.Title,
                        m.Status,
                        m.CreatedAt,
                        m.CompletedAt,
                        HasVideo = !string.IsNullOrEmpty(m.FinalVideoPath)
                    })
                    .ToListAsync();
                return Ok(memories);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("quota")]
        public async Task<ActionResult> GetUsageQuota()
        {
            try
            {
                var userId = GetUserId();
                var quota = await _subscriptionService.GetUsageQuotaAsync(userId);
                return Ok(quota);
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