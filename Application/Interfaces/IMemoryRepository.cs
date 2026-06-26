using Domain.Entities;

namespace Application.Interfaces
{
    public interface IMemoryRepository
    {
        Task<UserMemory?> GetByIdAsync(int id);
        Task<IEnumerable<UserMemory>> GetByUserIdAsync(int userId);
        Task<int> CreateAsync(UserMemory memory);
        Task<bool> UpdateAsync(UserMemory memory);
        Task<bool> DeleteAsync(int id);
        Task<UserMemory?> GetByShareTokenAsync(string shareToken);
        Task<IEnumerable<UserMemory>> GetPublicMemoriesAsync();
        Task IncrementViewCountAsync(int memoryId);
    }
}