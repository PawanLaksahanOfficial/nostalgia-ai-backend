using Domain.Entities;

namespace Application.Interfaces
{
    public interface IMemoryRepository
    {
        Task<UserMemory?> GetByIdAsync(int id);
        Task<UserMemory?> GetByIdForUserAsync(int id, int userId);
        Task<IEnumerable<UserMemory>> GetByUserIdAsync(int userId);
        Task<int> CreateAsync(UserMemory memory);
        Task<bool> UpdateAsync(UserMemory memory);
        Task<bool> DeleteAsync(int id);
        Task<IEnumerable<UserMemory>> GetPendingAsync(int max);
        Task<bool> TryClaimForProcessingAsync(int id);
        Task<int> RequeueStaleProcessingAsync(TimeSpan olderThan);
    }
}