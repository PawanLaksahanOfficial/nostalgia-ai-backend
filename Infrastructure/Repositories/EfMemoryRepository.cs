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

        public async Task<UserMemory?> GetByIdForUserAsync(int id, int userId)
        {
            return await _dbContext.UserMemories
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
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

        public async Task<IEnumerable<UserMemory>> GetPendingAsync(int max)
        {
            return await _dbContext.UserMemories
                .AsNoTracking()
                .Where(m => m.Status == VideoStatus.Pending)
                .OrderBy(m => m.CreatedAt)
                .Take(max)
                .ToListAsync();
        }

        public async Task<bool> TryClaimForProcessingAsync(int id)
        {
            var affected = await _dbContext.UserMemories
                .Where(m => m.Id == id && m.Status == VideoStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, VideoStatus.Processing)
                    .SetProperty(m => m.ProcessingStartedAt, DateTime.UtcNow)
                    .SetProperty(m => m.ProcessingStep, "Queued"));
            return affected > 0;
        }

        public async Task<int> RequeueStaleProcessingAsync(TimeSpan olderThan)
        {
            var cutoff = DateTime.UtcNow - olderThan;
            return await _dbContext.UserMemories
                .Where(m => m.Status == VideoStatus.Processing
                            && m.ProcessingStartedAt != null
                            && m.ProcessingStartedAt < cutoff)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, VideoStatus.Pending)
                    .SetProperty(m => m.ProcessingStartedAt, (DateTime?)null)
                    .SetProperty(m => m.ProcessingStep, (string?)null));
        }
    }
}