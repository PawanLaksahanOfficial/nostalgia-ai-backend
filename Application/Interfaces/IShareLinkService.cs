using Application.DTOs;

namespace Application.Interfaces
{
    public interface IShareLinkService
    {
        Task<ShareLinkDto> CreateAsync(int memoryId, int userId, int? expiresInDays, string? label);
        Task<IEnumerable<ShareLinkDto>> GetForMemoryAsync(int memoryId, int userId);
        Task RevokeAsync(int shareLinkId, int userId);
        Task<PublicVideoDto?> ResolveAsync(string token);
        Task<ShareMediaTarget?> ResolveMediaAsync(string token, ShareMediaKind kind);
    }
}
