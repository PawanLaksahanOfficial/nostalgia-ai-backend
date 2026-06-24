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
    }
}