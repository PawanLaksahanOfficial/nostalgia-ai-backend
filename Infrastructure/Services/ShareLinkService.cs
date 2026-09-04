using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class ShareLinkService : IShareLinkService
    {
        private const int TokenCollisionRetries = 3;

        private readonly IShareLinkRepository _shareLinkRepository;
        private readonly IMemoryRepository _memoryRepository;
        private readonly ILogger<ShareLinkService> _logger;
        private readonly string _frontendBaseUrl;

        public ShareLinkService(
            IShareLinkRepository shareLinkRepository,
            IMemoryRepository memoryRepository,
            IConfiguration configuration,
            ILogger<ShareLinkService> logger)
        {
            _shareLinkRepository = shareLinkRepository;
            _memoryRepository = memoryRepository;
            _logger = logger;
            _frontendBaseUrl = (configuration.GetSection("Frontend")["BaseUrl"] ?? "http://localhost:5173")
                .TrimEnd('/');
        }

        public async Task<ShareLinkDto> CreateAsync(int memoryId, int userId, int? expiresInDays, string? label)
        {
            var memory = await _memoryRepository.GetByIdForUserAsync(memoryId, userId)
                ?? throw new InvalidOperationException("Video not found.");

            if (memory.Status != VideoStatus.Completed)
            {
                throw new InvalidOperationException("Only completed videos can be shared.");
            }
            if (string.IsNullOrEmpty(memory.FinalVideoPath))
            {
                throw new InvalidOperationException("This video has no playable file yet.");
            }

            var shareLink = new MemoryShareLink
            {
                UserMemoryId = memoryId,
                CreatedByUserId = userId,
                Label = label,
                ExpiresAt = expiresInDays.HasValue
                    ? DateTime.UtcNow.AddDays(expiresInDays.Value)
                    : null,
                CreatedAt = DateTime.UtcNow
            };
            await PersistWithFreshTokenAsync(shareLink);
            if (!memory.IsPublic)
            {
                memory.IsPublic = true;
                await _memoryRepository.UpdateAsync(memory);
            }
            return ToDto(shareLink);
        }

        public async Task<IEnumerable<ShareLinkDto>> GetForMemoryAsync(int memoryId, int userId)
        {
            _ = await _memoryRepository.GetByIdForUserAsync(memoryId, userId)
                ?? throw new InvalidOperationException("Video not found.");

            var links = await _shareLinkRepository.GetByMemoryIdAsync(memoryId);
            return links.Select(ToDto).ToList();
        }

        public async Task RevokeAsync(int shareLinkId, int userId)
        {
            var shareLink = await _shareLinkRepository.GetByIdForUserAsync(shareLinkId, userId)
                ?? throw new InvalidOperationException("Share link not found.");

            if (!shareLink.IsRevoked)
            {
                shareLink.IsRevoked = true;
                await _shareLinkRepository.UpdateAsync(shareLink);
            }
            if (!await _shareLinkRepository.AnyActiveForMemoryAsync(shareLink.UserMemoryId))
            {
                var memory = await _memoryRepository.GetByIdForUserAsync(shareLink.UserMemoryId, userId);
                if (memory is { IsPublic: true })
                {
                    memory.IsPublic = false;
                    await _memoryRepository.UpdateAsync(memory);
                }
            }
        }

        public async Task<PublicVideoDto?> ResolveAsync(string token)
        {
            var shareLink = await GetUsableLinkAsync(token);
            if (shareLink == null)
            {
                return null;
            }
            await _shareLinkRepository.RecordViewAsync(shareLink.Id);
            var memory = shareLink.UserMemory;
            return new PublicVideoDto
            {
                Token = shareLink.Token,
                Title = memory.Title,
                Narrative = memory.GeneratedNarrative,
                DurationSeconds = memory.DurationSeconds,
                OwnerFirstName = memory.User?.FirstName ?? string.Empty,
                CreatedAt = memory.CreatedAt,
                ViewCount = shareLink.ViewCount + 1
            };
        }

        public async Task<ShareMediaTarget?> ResolveMediaAsync(string token, ShareMediaKind kind)
        {
            var shareLink = await GetUsableLinkAsync(token);
            if (shareLink == null)
            {
                return null;
            }
            var memory = shareLink.UserMemory;
            var storageKey = kind == ShareMediaKind.Thumbnail
                ? memory.ThumbnailPath
                : memory.FinalVideoPath;

            if (string.IsNullOrEmpty(storageKey))
            {
                return null;
            }
            return new ShareMediaTarget
            {
                MemoryId = memory.Id,
                StorageKey = storageKey,
                ContentType = kind == ShareMediaKind.Thumbnail
                    ? "image/jpeg"
                    : memory.ContentType ?? "video/mp4",
                FileName = kind == ShareMediaKind.Thumbnail
                    ? $"{FileNameSlug.Create(memory.Title)}.jpg"
                    : $"{FileNameSlug.Create(memory.Title)}.mp4"
            };
        }

        private async Task<MemoryShareLink?> GetUsableLinkAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }
            var shareLink = await _shareLinkRepository.GetByTokenAsync(token);
            if (shareLink == null ||
                shareLink.IsRevoked ||
                (shareLink.ExpiresAt.HasValue && shareLink.ExpiresAt.Value <= DateTime.UtcNow) ||
                shareLink.UserMemory == null ||
                shareLink.UserMemory.Status != VideoStatus.Completed)
            {
                return null;
            }
            return shareLink;
        }

        private async Task PersistWithFreshTokenAsync(MemoryShareLink shareLink)
        {
            for (var attempt = 1; ; attempt++)
            {
                shareLink.Token = ShareTokenGenerator.Create();
                try
                {
                    await _shareLinkRepository.CreateAsync(shareLink);
                    return;
                }
                catch (DbUpdateException) when (attempt < TokenCollisionRetries)
                {
                    _logger.LogWarning("Share token insert failed on attempt {Attempt}; retrying with a new token.", attempt);
                }
            }
        }

        private ShareLinkDto ToDto(MemoryShareLink shareLink) => new()
        {
            Id = shareLink.Id,
            Token = shareLink.Token,
            ShareUrl = $"{_frontendBaseUrl}/s/{shareLink.Token}",
            Label = shareLink.Label,
            ExpiresAt = shareLink.ExpiresAt,
            IsRevoked = shareLink.IsRevoked,
            IsExpired = shareLink.ExpiresAt.HasValue && shareLink.ExpiresAt.Value <= DateTime.UtcNow,
            ViewCount = shareLink.ViewCount,
            CreatedAt = shareLink.CreatedAt
        };
    }
}
