using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUserRepository _userRepository;
        private readonly IAuthenticationService _authenticationService;
        private readonly IEmailService _emailService;

        public AuthController(
            IPasswordHasher passwordHasher,
            IUserRepository userRepository,
            IAuthenticationService authenticationService,
            IEmailService emailService)
        {
            _passwordHasher = passwordHasher;
            _userRepository = userRepository;
            _authenticationService = authenticationService;
            _emailService = emailService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var existingUser = await _userRepository.GetUserByEmailIncludingDeletedAsync(request.Email);

                // If user exists and is active, return conflict
                if (existingUser != null && !existingUser.Deleted)
                {
                    return Conflict(ApiResponse<AuthResponse>.Fail("Email already registered."));
                }

                var passwordHash = _passwordHasher.Hash(request.Password);

                // If user exists but is deleted, reactivate
                if (existingUser != null && existingUser.Deleted)
                {
                    var reactivatedUser = await _userRepository.ReactivateDeletedUserAsync(existingUser, request, passwordHash);
                    if (reactivatedUser == null)
                    {
                        return BadRequest(ApiResponse<AuthResponse>.Fail("Failed to reactivate account."));
                    }

                    var response = CreateAuthResponse(reactivatedUser);
                    return Ok(ApiResponse<AuthResponse>.Ok(response, "Account reactivated successfully."));
                }

                // Create new user
                var newUser = await _userRepository.RegisterUserAsync(request, passwordHash);
                if (newUser == null)
                {
                    return BadRequest(ApiResponse<AuthResponse>.Fail("Failed to create account."));
                }

                var authResponse = CreateAuthResponse(newUser);
                return Ok(ApiResponse<AuthResponse>.Ok(authResponse, "Registration successful."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<AuthResponse>.Fail(ex.Message));
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
        {
            try
            {
                var user = await _userRepository.GetUserByEmailAsync(request.Email);
                if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid email or password."));
                }

                if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
                {
                    return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid email or password."));
                }

                var loginUpdated = await _userRepository.UpdateUserLoginStatusAsync(user);
                if (!loginUpdated)
                {
                    return BadRequest(ApiResponse<AuthResponse>.Fail("Failed to update login status."));
                }

                var response = CreateAuthResponse(user);
                return Ok(ApiResponse<AuthResponse>.Ok(response, "Login successful."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<AuthResponse>.Fail(ex.Message));
            }
        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult<ApiResponse<object>>> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                var user = await _userRepository.GetUserByEmailAsync(request.Email);
                if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    // Return success to prevent email enumeration
                    return Ok(ApiResponse<object>.Ok(new { }, "If an account exists, a reset email has been sent."));
                }

                var resetToken = new PasswordResetToken
                {
                    UserId = user.UserId,
                    Token = Guid.NewGuid().ToString("N"),
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Used = false,
                    CreatedAt = DateTime.UtcNow
                };

                var tokenCreated = await _userRepository.CreatePasswordResetTokenAsync(resetToken);
                if (!tokenCreated)
                {
                    return BadRequest(ApiResponse<object>.Fail("Failed to generate reset token."));
                }
                var emailSent = await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken.Token, $"{user.FirstName} {user.LastName}");
                if (!emailSent)
                {
                    return BadRequest(ApiResponse<object>.Fail("Failed to send reset email. Please try again."));
                }

                return Ok(ApiResponse<object>.Ok(new { }, "A reset email has been sent."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult<ApiResponse<object>>> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                var resetToken = await _userRepository.ValidatePasswordResetTokenAsync(request.Email, request.Token);
                if (resetToken == null)
                {
                    return BadRequest(ApiResponse<object>.Fail("Invalid or expired reset token."));
                }
                var passwordHash = _passwordHasher.Hash(request.NewPassword);
                var passwordUpdated = await _userRepository.UpdatePasswordAsync(resetToken.UserId, passwordHash);
                if (!passwordUpdated)
                {
                    return BadRequest(ApiResponse<object>.Fail("Failed to update password."));
                }
                var tokenMarked = await _userRepository.MarkResetTokenAsUsedAsync(resetToken.Id);
                if (!tokenMarked)
                {
                    return BadRequest(ApiResponse<object>.Fail("Failed to mark token as used."));
                }

                return Ok(ApiResponse<object>.Ok(new { }, "Password reset successful."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail(ex.Message));
            }
        }

        private AuthResponse CreateAuthResponse(User user)
        {
            var isPremium = user.Tier == UserTier.Premium;
            return new AuthResponse
            {
                Token = _authenticationService.GenerateJwtToken(user.UserId),
                User = new UserDto
                {
                    UserId = user.UserId,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    AvatarUrl = user.AvatarUrl,
                    Tier = user.Tier.ToString().ToLower(),
                    MonthlyMemoriesUsed = user.MonthlyMemoryCount,
                    MonthlyMemoriesLimit = isPremium ? 100 : 3
                }
            };
        }
    }
}