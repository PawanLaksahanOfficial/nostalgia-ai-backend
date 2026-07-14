using Application.DTOs;
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
                .Include(m => m.Likes)
                .Include(m => m.Comments)
                    .ThenInclude(c => c.User)
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
                .Include(m => m.Likes)
                .Include(m => m.Comments)
                    .ThenInclude(c => c.User)
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

        public async Task<bool> ToggleLikeAsync(int memoryId, int userId)
        {
            var existingLike = await _dbContext.Likes
                .FirstOrDefaultAsync(l => l.MemoryId == memoryId && l.UserId == userId);
            if (existingLike != null)
            {
                _dbContext.Likes.Remove(existingLike);
                await _dbContext.SaveChangesAsync();
                return false;
            }
            else
            {
                var like = new Like
                {
                    MemoryId = memoryId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.Likes.Add(like);
                await _dbContext.SaveChangesAsync();
                return true;
            }
        }

        public async Task<int> GetLikeCountAsync(int memoryId)
        {
            return await _dbContext.Likes.CountAsync(l => l.MemoryId == memoryId);
        }

        public async Task<bool> IsLikedByUserAsync(int memoryId, int userId)
        {
            return await _dbContext.Likes.AnyAsync(l => l.MemoryId == memoryId && l.UserId == userId);
        }

        public async Task<Comment> AddCommentAsync(Comment comment)
        {
            _dbContext.Comments.Add(comment);
            await _dbContext.SaveChangesAsync();
            return comment;
        }

        public async Task<IEnumerable<Comment>> GetCommentsByMemoryIdAsync(int memoryId)
        {
            return await _dbContext.Comments
                .Include(c => c.User)
                .Where(c => c.MemoryId == memoryId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> DeleteCommentAsync(int commentId, int userId)
        {
            var comment = await _dbContext.Comments.FindAsync(commentId);
            if (comment == null || comment.UserId != userId) return false;           
            _dbContext.Comments.Remove(comment);
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }

        public async Task<Collection> CreateCollectionAsync(Collection collection)
        {
            _dbContext.Collections.Add(collection);
            await _dbContext.SaveChangesAsync();
            return collection;
        }

        public async Task<IEnumerable<Collection>> GetUserCollectionsAsync(int userId)
        {
            return await _dbContext.Collections
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<Collection?> GetCollectionByIdAsync(int collectionId, int userId)
        {
            return await _dbContext.Collections
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);
        }

        public async Task<bool> UpdateCollectionAsync(Collection collection)
        {
            _dbContext.Collections.Update(collection);
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }

        public async Task<bool> DeleteCollectionAsync(int collectionId, int userId)
        {
            var collection = await _dbContext.Collections
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);
            if (collection == null) return false;
            _dbContext.Collections.Remove(collection);
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }

        public async Task<bool> AddMemoryToCollectionAsync(int collectionId, int memoryId)
        {
            var collection = await _dbContext.Collections
                .Include(c => c.Memories)
                .FirstOrDefaultAsync(c => c.Id == collectionId);
            
            if (collection == null) return false;
            var memory = await _dbContext.UserMemories.FindAsync(memoryId);
            if (memory == null) return false;
            if (!collection.Memories.Any(m => m.Id == memoryId))
            {
                collection.Memories.Add(memory);
                await _dbContext.SaveChangesAsync();
            }
            return true;
        }

        public async Task<bool> RemoveMemoryFromCollectionAsync(int collectionId, int memoryId)
        {
            var collection = await _dbContext.Collections
                .Include(c => c.Memories)
                .FirstOrDefaultAsync(c => c.Id == collectionId); 
            if (collection == null) return false;
            var memory = collection.Memories.FirstOrDefault(m => m.Id == memoryId);
            if (memory == null) return false;
            collection.Memories.Remove(memory);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<UserMemory>> GetMemoriesInCollectionAsync(int collectionId)
        {
            var collection = await _dbContext.Collections
                .Include(c => c.Memories)
                .FirstOrDefaultAsync(c => c.Id == collectionId);           
            return collection?.Memories.OrderByDescending(m => m.CreatedAt).ToList() ?? new List<UserMemory>();
        }
        public async Task<Gift> CreateGiftAsync(Gift gift)
        {
            _dbContext.Gifts.Add(gift);
            await _dbContext.SaveChangesAsync();
            return gift;
        }

        public async Task<IEnumerable<Gift>> GetSentGiftsAsync(int userId)
        {
            return await _dbContext.Gifts
                .Include(g => g.Memory)
                .Include(g => g.Sender)
                .Where(g => g.SenderId == userId)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Gift>> GetReceivedGiftsAsync(string email)
        {
            return await _dbContext.Gifts
                .Include(g => g.Memory)
                .Include(g => g.Sender)
                .Where(g => g.RecipientEmail == email)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();
        }

        public async Task<Gift?> GetGiftByIdAsync(int giftId)
        {
            return await _dbContext.Gifts
                .Include(g => g.Memory)
                .Include(g => g.Sender)
                .FirstOrDefaultAsync(g => g.Id == giftId);
        }

        public async Task<bool> MarkGiftAsOpenedAsync(int giftId)
        {
            var gift = await _dbContext.Gifts.FindAsync(giftId);
            if (gift == null) return false;          
            gift.IsOpened = true;
            gift.OpenedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return true;
        }
    }
}
