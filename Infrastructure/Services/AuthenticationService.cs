using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Google.Apis.Auth;
using System.Net.Http;
using Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace Infrastructure.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly EFDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            EFDbContext dbContext,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<AuthenticationService> logger)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public static IReadOnlyList<string> ParseGoogleClientIds(string? configured) =>
            (configured ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        public async Task<GoogleJsonWebSignature.Payload?> ValidateGoogleAuthenticationTokenAsync(string token)
        {
            var clientIds = ParseGoogleClientIds(_configuration["GoogleClientId"]);
            if (clientIds.Count == 0)
            {
                _logger.LogWarning("Google sign-in rejected: GoogleClientId is not configured.");
                return null;
            }

            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = clientIds
            };

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(token, settings);
            }
            catch (InvalidJwtException ex)
            {
                // Client IDs are public, so logging them makes an audience mismatch obvious.
                _logger.LogWarning("Google sign-in rejected: {Reason} Accepted client IDs: {ClientIds}.",
                    ex.Message, string.Join(", ", clientIds));
                return null;
            }
            if (payload == null || string.IsNullOrEmpty(payload.Email))
            {
                _logger.LogWarning("Google sign-in rejected: the token has no email address.");
                return null;
            }
            if (!payload.EmailVerified)
            {
                _logger.LogWarning("Google sign-in rejected: the Google account's email is not verified.");
                return null;
            }
            return payload;
        }

        public async Task<MetaUserDto?> ValidateMetaAuthenticationTokenAsync(string token)
        {
            var appId = _configuration["Meta:AppId"];
            var appSecret = _configuration["Meta:AppSecret"];
            if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(appSecret))
            {
                var appAccessToken = $"{appId}|{appSecret}";
                var debugUrl = $"https://graph.facebook.com/debug_token?input_token={token}&access_token={appAccessToken}";
                var client = _httpClientFactory.CreateClient();
                var debugResponse = await client.GetAsync(debugUrl);
                if (!debugResponse.IsSuccessStatusCode)
                {
                    return null;
                }
                var debugContent = await debugResponse.Content.ReadAsStringAsync();
                try
                {
                    using var debugJson = JsonDocument.Parse(debugContent);
                    if (!debugJson.RootElement.TryGetProperty("data", out var data) ||
                        !data.TryGetProperty("is_valid", out var isValid) ||
                        !isValid.GetBoolean() ||
                        !data.TryGetProperty("app_id", out var tokenAppId) ||
                        tokenAppId.GetString() != appId)
                    {
                        return null;
                    }
                }
                catch (JsonException)
                {
                    return null;
                }
                var meUrl = $"https://graph.facebook.com/v18.0/me?fields=id,name,email,picture&access_token={token}";
                var meResponse = await client.GetAsync(meUrl);
                if (!meResponse.IsSuccessStatusCode)
                {
                    return null;
                }
                var meContent = await meResponse.Content.ReadAsStringAsync();

                MetaUserDto? profile;
                try
                {
                    profile = JsonSerializer.Deserialize<MetaUserDto>(meContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException)
                {
                    return null;
                }
                if (profile == null || string.IsNullOrEmpty(profile.Email))
                {
                    return null;
                }
                return profile;
            }
            return null;
        }

        public string GenerateJwtToken(int userId)
        {
            var key = _configuration["JwtSettings:Key"];
            if (!string.IsNullOrEmpty(key))
            {
                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
                var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
                var claims = new[]
                {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),//for banned list purposes
                };
                var token = new JwtSecurityToken(
                    issuer: _configuration["JwtSettings:Issuer"],
                    audience: _configuration["JwtSettings:Audience"],
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["JwtSettings:DurationInMinutes"])),
                    signingCredentials: credentials);
                var jwtToken = new JwtSecurityTokenHandler().WriteToken(token);
                return jwtToken;
            }
            return string.Empty;
        }

        public AuthResponse BuildAuthResponse(User user)
        {
            var isPremium = user.IsPremiumActive();
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
                    Tier = isPremium ? "premium" : "free",
                    MonthlyMemoriesUsed = user.MonthlyMemoryCount,
                    MonthlyMemoriesLimit = isPremium
                        ? _configuration.GetValue("TierLimits:Premium:MonthlyMemories", 100)
                        : _configuration.GetValue("TierLimits:Free:MonthlyMemories", 3)
                }
            };
        }
    }
}
