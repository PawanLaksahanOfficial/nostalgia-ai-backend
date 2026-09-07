using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class EfShareLinkRepository : IShareLinkRepository
    {
        private readonly EFDbContext _dbContext;

        public EfShareLinkRepository(EFDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<MemoryShareLink?> GetByTokenAsync(string token)
        {
            return await _dbContext.MemoryShareLinks
                .AsNoTracking()
                .Include(s => s.UserMemory)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(s => s.Token == token);
        }

        public async Task<MemoryShareLink?> GetByIdForUserAsync(int shareLinkId, int userId)
        {
            return await _dbContext.MemoryShareLinks
                .Include(s => s.UserMemory)
                .FirstOrDefaultAsync(s => s.Id == shareLinkId && s.UserMemory.UserId == userId);
        }

        public async Task<IEnumerable<MemoryShareLink>> GetByMemoryIdAsync(int memoryId)
        {
            return await _dbContext.MemoryShareLinks
                .Where(s => s.UserMemoryId == memoryId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> AnyActiveForMemoryAsync(int memoryId)
        {
            var now = DateTime.UtcNow;
            return await _dbContext.MemoryShareLinks
                .AnyAsync(s => s.UserMemoryId == memoryId && !s.IsRevoked && (s.ExpiresAt == null || s.ExpiresAt > now));
        }

        public async Task<int> CreateAsync(MemoryShareLink shareLink)
        {
            _dbContext.MemoryShareLinks.Add(shareLink);
            await _dbContext.SaveChangesAsync();
            return shareLink.Id;
        }

        public async Task<bool> UpdateAsync(MemoryShareLink shareLink)
        {
            _dbContext.MemoryShareLinks.Update(shareLink);
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }

        public async Task RecordViewAsync(int shareLinkId)
        {
            await _dbContext.MemoryShareLinks
                .Where(s => s.Id == shareLinkId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.ViewCount, s => s.ViewCount + 1)
                    .SetProperty(s => s.LastViewedAt, DateTime.UtcNow));

            await _dbContext.UserMemories
                .Where(m => m.ShareLinks.Any(s => s.Id == shareLinkId))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.ViewCount, m => m.ViewCount + 1));
        }
    }
}
