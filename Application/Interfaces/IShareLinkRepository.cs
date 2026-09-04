using Domain.Entities;

namespace Application.Interfaces
{
    public interface IShareLinkRepository
    {
        Task<MemoryShareLink?> GetByTokenAsync(string token);
        Task<MemoryShareLink?> GetByIdForUserAsync(int shareLinkId, int userId);
        Task<IEnumerable<MemoryShareLink>> GetByMemoryIdAsync(int memoryId);
        Task<bool> AnyActiveForMemoryAsync(int memoryId);
        Task<int> CreateAsync(MemoryShareLink shareLink);
        Task<bool> UpdateAsync(MemoryShareLink shareLink);
        Task RecordViewAsync(int shareLinkId);
    }
}
