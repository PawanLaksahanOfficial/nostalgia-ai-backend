using System.Security.Cryptography;
using System.Text;
using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private const int TokenBytes = 32;

        private readonly EFDbContext _dbContext;
        private readonly ILogger<RefreshTokenService> _logger;
        private readonly TimeSpan _lifetime;

        public RefreshTokenService(EFDbContext dbContext, IConfiguration configuration, ILogger<RefreshTokenService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
            _lifetime = TimeSpan.FromDays(configuration.GetValue("JwtSettings:RefreshTokenDays", 30));
        }

        public async Task<string> IssueAsync(int userId)
        {
            var now = DateTime.UtcNow;
            // Expired tokens can't be used or replayed any more, so there is no reason to keep them.
            await _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.ExpiresAt <= now)
                .ExecuteDeleteAsync();
            var token = Add(userId, Guid.NewGuid(), now);
            await _dbContext.SaveChangesAsync();
            return token;
        }

        public async Task<RotatedRefreshToken?> RotateAsync(string refreshToken)
        {
            var hash = Hash(refreshToken);
            var now = DateTime.UtcNow;
            var record = await _dbContext.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (record == null || record.ExpiresAt <= now)
            {
                return null;
            }

            if (record.RevokedAt != null)
            {
                // A token that was already swapped came back, so someone else holds a copy: end the whole session.
                _logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking that session.", record.UserId);
                await RevokeFamilyAsync(record.FamilyId, now);
                return null;
            }

            // Only one request can claim the token, so two simultaneous refreshes can't both get a successor.
            var claimed = await _dbContext.RefreshTokens
                .Where(t => t.Id == record.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
            if (claimed == 0)
            {
                return null;
            }

            var successor = Add(record.UserId, record.FamilyId, now);
            await _dbContext.SaveChangesAsync();
            return new RotatedRefreshToken { UserId = record.UserId, RefreshToken = successor };
        }

        public async Task RevokeAsync(string refreshToken)
        {
            var hash = Hash(refreshToken);
            var familyId = await _dbContext.RefreshTokens
                .Where(t => t.TokenHash == hash)
                .Select(t => (Guid?)t.FamilyId)
                .FirstOrDefaultAsync();
            if (familyId != null)
            {
                await RevokeFamilyAsync(familyId.Value, DateTime.UtcNow);
            }
        }

        public async Task RevokeAllAsync(int userId)
        {
            var now = DateTime.UtcNow;
            await _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
        }

        public static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private string Add(int userId, Guid familyId, DateTime now)
        {
            var token = ShareTokenGenerator.Create(TokenBytes);
            _dbContext.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = Hash(token),
                FamilyId = familyId,
                ExpiresAt = now + _lifetime,
                CreatedAt = now
            });
            return token;
        }

        private async Task RevokeFamilyAsync(Guid familyId, DateTime now)
        {
            await _dbContext.RefreshTokens
                .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
        }
    }
}
