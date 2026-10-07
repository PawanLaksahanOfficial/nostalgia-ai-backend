using System.Security.Claims;
using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUserRepository _userRepository;
        private readonly IAuthenticationService _authenticationService;
        private readonly IEmailService _emailService;
        private readonly IpAddressHasher _ipAddressHasher;
        private readonly DisposableEmailDomains _disposableEmailDomains;
        private readonly int _perIpDailySignupLimit;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IPasswordHasher passwordHasher,
            IUserRepository userRepository,
            IAuthenticationService authenticationService,
            IEmailService emailService,
            IpAddressHasher ipAddressHasher,
            DisposableEmailDomains disposableEmailDomains,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _passwordHasher = passwordHasher;
            _userRepository = userRepository;
            _authenticationService = authenticationService;
            _emailService = emailService;
            _ipAddressHasher = ipAddressHasher;
            _disposableEmailDomains = disposableEmailDomains;
            _perIpDailySignupLimit = configuration.GetSection("Abuse").GetValue("PerIpDailySignupLimit", 3);
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request)
        {
            if (_disposableEmailDomains.IsDisposable(request.Email))
            {
                return BadRequest(ApiResponse<AuthResponse>.Fail("Please sign up with a permanent email address."));
            }
            var signupIpHash = _ipAddressHasher.Hash(HttpContext.Connection.RemoteIpAddress);
            try
            {
                var existingUser = await _userRepository.GetUserByEmailIncludingDeletedAsync(request.Email);

                // If user exists and is active, return conflict
                if (existingUser != null && !existingUser.Deleted)
                {
                    return Conflict(ApiResponse<AuthResponse>.Fail("Email already registered."));
                }

                // Gmail dots and "+tag" aliases reach the same inbox, so they count as the same account.
                if (existingUser == null && await _userRepository.GetUserByCanonicalEmailAsync(request.Email) != null)
                {
                    return Conflict(ApiResponse<AuthResponse>.Fail("Email already registered."));
                }

                if (signupIpHash != null && _perIpDailySignupLimit > 0 &&
                    await _userRepository.CountUsersCreatedFromIpSinceAsync(signupIpHash, DateTime.UtcNow.AddDays(-1)) >= _perIpDailySignupLimit)
                {
                    return StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<AuthResponse>.Fail(
                        "Too many accounts were created from your network today. Please try again tomorrow."));
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

                    await SendVerificationEmailAsync(reactivatedUser);
                    var response = CreateAuthResponse(reactivatedUser);
                    return Ok(ApiResponse<AuthResponse>.Ok(response, "Account reactivated. Check your inbox to verify your email."));
                }

                // Create new user
                var newUser = await _userRepository.RegisterUserAsync(request, passwordHash, signupIpHash);
                if (newUser == null)
                {
                    return BadRequest(ApiResponse<AuthResponse>.Fail("Failed to create account."));
                }

                await SendVerificationEmailAsync(newUser);
                var authResponse = CreateAuthResponse(newUser);
                return Ok(ApiResponse<AuthResponse>.Ok(authResponse, "Registration successful. Check your inbox to verify your email."));
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                return Conflict(ApiResponse<AuthResponse>.Fail("Email already registered."));
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
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

        [HttpPost("forgot-password")]
        public async Task<ActionResult<ApiResponse<object>>> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            const string genericMessage = "If an account exists, a reset email has been sent.";
            try
            {
                var user = await _userRepository.GetUserByEmailAsync(request.Email);
                if (user != null && !string.IsNullOrEmpty(user.PasswordHash))
                {
                    var resetToken = new PasswordResetToken
                    {
                        UserId = user.UserId,
                        Token = Guid.NewGuid().ToString("N"),
                        ExpiresAt = DateTime.UtcNow.AddHours(1),
                        Used = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    var tokenCreated = await _userRepository.CreatePasswordResetTokenAsync(resetToken);
                    if (tokenCreated)
                    {
                        var emailSent = await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken.Token, $"{user.FirstName} {user.LastName}");
                        if (!emailSent)
                        {
                            _logger.LogError("Failed to send password reset email for user {UserId}.", user.UserId);
                        }
                    }
                    else
                    {
                        _logger.LogError("Failed to create password reset token for user {UserId}.", user.UserId);
                    }
                }

                return Ok(ApiResponse<object>.Ok(new { }, genericMessage));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ForgotPassword.");
                return Ok(ApiResponse<object>.Ok(new { }, genericMessage));
            }
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult<ApiResponse<object>>> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var resetToken = await _userRepository.ValidatePasswordResetTokenAsync(request.Email, request.Token);
            if (resetToken == null)
            {
                return BadRequest(ApiResponse<object>.Fail("Invalid or expired reset token."));
            }

            var passwordHash = _passwordHasher.Hash(request.NewPassword);
            var passwordReset = await _userRepository.ResetPasswordAsync(resetToken.UserId, passwordHash);
            if (!passwordReset)
            {
                return BadRequest(ApiResponse<object>.Fail("Failed to update password."));
            }

            return Ok(ApiResponse<object>.Ok(new { }, "Password reset successful."));
        }

        [HttpPost("verify-email")]
        public async Task<ActionResult<ApiResponse<object>>> VerifyEmail([FromBody] VerifyEmailRequest request)
        {
            if (!await _userRepository.VerifyEmailAsync(request.Email, request.Token))
            {
                return BadRequest(ApiResponse<object>.Fail(
                    "This verification link is invalid or has expired. Sign in to get a new one."));
            }
            return Ok(ApiResponse<object>.Ok(new { }, "Your email is verified. You can create videos now."));
        }

        [HttpPost("resend-verification")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> ResendVerification()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = int.TryParse(userIdClaim, out var userId) ? await _userRepository.GetByIdAsync(userId) : null;
            if (user == null)
            {
                return Unauthorized(ApiResponse<object>.Fail("Invalid or missing authentication token."));
            }
            if (user.EmailVerified)
            {
                return Ok(ApiResponse<object>.Ok(new { }, "Your email is already verified."));
            }
            await SendVerificationEmailAsync(user);
            return Ok(ApiResponse<object>.Ok(new { }, $"We sent a new link to {user.Email}."));
        }

        // A failed send is logged rather than failing sign-up: the user can ask for another link.
        private async Task SendVerificationEmailAsync(User user)
        {
            var token = new EmailVerificationToken
            {
                UserId = user.UserId,
                Token = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow
            };
            if (!await _userRepository.CreateEmailVerificationTokenAsync(token))
            {
                _logger.LogError("Failed to create an email verification token for user {UserId}.", user.UserId);
                return;
            }
            if (!await _emailService.SendEmailVerificationAsync(user.Email, token.Token, $"{user.FirstName} {user.LastName}"))
            {
                _logger.LogError("Failed to send the verification email for user {UserId}.", user.UserId);
            }
        }

        private AuthResponse CreateAuthResponse(User user) =>
            _authenticationService.BuildAuthResponse(user);

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
        }
    }
}