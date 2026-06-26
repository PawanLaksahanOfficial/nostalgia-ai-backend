using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class EfMemoryRepository : IMemoryRepository
    {
        private readonly EFDbContext _dbContext;

        public EfMemoryRepository(EFDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<UserMemory?> GetByIdAsync(int id)
        {
            return await _dbContext.UserMemories
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<IEnumerable<UserMemory>> GetByUserIdAsync(int userId)
        {
            return await _dbContext.UserMemories
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<int> CreateAsync(UserMemory memory)
        {
            _dbContext.UserMemories.Add(memory);
            await _dbContext.SaveChangesAsync();
            return memory.Id;
        }

        public async Task<bool> UpdateAsync(UserMemory memory)
        {
            _dbContext.UserMemories.Update(memory);
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var memory = await _dbContext.UserMemories.FindAsync(id);
            if (memory == null) return false;
            
            _dbContext.UserMemories.Remove(memory);
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }

        public async Task<UserMemory?> GetByShareTokenAsync(string shareToken)
        {
            return await _dbContext.UserMemories
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.ShareToken == shareToken && m.IsPublic);
        }

        public async Task<IEnumerable<UserMemory>> GetPublicMemoriesAsync()
        {
            return await _dbContext.UserMemories
                .Where(m => m.IsPublic && m.Status == VideoStatus.Completed)
                .OrderByDescending(m => m.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task IncrementViewCountAsync(int memoryId)
        {
            var memory = await _dbContext.UserMemories.FindAsync(memoryId);
            if (memory == null) return;
            memory.ViewCount++;
            await _dbContext.SaveChangesAsync();
        }
    }
}