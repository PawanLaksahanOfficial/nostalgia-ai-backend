using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly EFDbContext _dbContext;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IConfiguration _configuration;
        private readonly IUserRepository _userRepository;

        public AuthController(EFDbContext dbContext, IPasswordHasher passwordHasher, IConfiguration configuration, IUserRepository userRepository)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
            _configuration = configuration;
            _userRepository = userRepository;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var existingUser = await _userRepository.GetUserByEmailIncludingDeletedAsync(request.Email);
                if (existingUser != null && !existingUser.Deleted)
                {
                    return BadRequest("Email already registered.");
                }
                if (existingUser != null && existingUser.Deleted)
                {
                    existingUser.FirstName = request.FirstName;
                    existingUser.LastName = request.LastName;
                    existingUser.PasswordHash = _passwordHasher.Hash(request.Password);
                    existingUser.Deleted = false;
                    existingUser.Active = true;
                    existingUser.LastUpdatedDate = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    return Ok(CreateAuthResponse(existingUser));
                }
                var user = new User
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    PasswordHash = _passwordHasher.Hash(request.Password),
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow,
                    LastSignInDate = DateTime.UtcNow,
                    Active = true,
                    Deleted = false,
                    Tier = UserTier.Free,
                    MonthlyMemoryCount = 0,
                    MonthlyCountResetDate = DateTime.UtcNow
                };
                _dbContext.Users.Add(user);
                await _dbContext.SaveChangesAsync();
                return Ok(CreateAuthResponse(user));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            try
            {
                var user = await _userRepository.GetUserByEmailAsync(request.Email);
                if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    return Unauthorized("Invalid email or password.");
                }
                if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
                {
                    return Unauthorized("Invalid email or password.");
                }
                user.LastSignInDate = DateTime.UtcNow;
                user.LastUpdatedDate = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return Ok(CreateAuthResponse(user));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                var user = await _userRepository.GetUserByEmailAsync(request.Email);
                if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    return Ok(new { message = "If an account exists, a reset email has been sent." });
                }
                var resetToken = new PasswordResetToken
                {
                    UserId = user.UserId,
                    Token = Guid.NewGuid().ToString("N"),
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Used = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _userRepository.CreatePasswordResetTokenAsync(resetToken);
                // TODO: Send email with reset link
                // For now, return the token in dev mode
                return Ok(new { message = "Reset token generated.", token = resetToken.Token });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                var resetToken = await _userRepository.ValidatePasswordResetTokenAsync(request.Email, request.Token);
                if (resetToken == null)
                {
                    return BadRequest("Invalid or expired reset token.");
                }
                var passwordHash = _passwordHasher.Hash(request.NewPassword);
                await _userRepository.UpdatePasswordAsync(resetToken.UserId, passwordHash);
                await _userRepository.MarkResetTokenAsUsedAsync(resetToken.Id);
                return Ok(new { message = "Password reset successful." });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private AuthResponse CreateAuthResponse(User user)
        {
            var isPremium = user.Tier == UserTier.Premium;
            return new AuthResponse
            {
                Token = GenerateJwtToken(user.UserId),
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

        private string GenerateJwtToken(int userId)
        {
            var key = _configuration["JwtSettings:Key"];
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            var token = new JwtSecurityToken(
                issuer: _configuration["JwtSettings:Issuer"],
                audience: _configuration["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["JwtSettings:DurationInMinutes"])),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}