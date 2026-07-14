using Application.DTOs;
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
        Task<bool> ToggleLikeAsync(int memoryId, int userId);
        Task<int> GetLikeCountAsync(int memoryId);
        Task<bool> IsLikedByUserAsync(int memoryId, int userId);
        Task<Comment> AddCommentAsync(Comment comment);
        Task<IEnumerable<Comment>> GetCommentsByMemoryIdAsync(int memoryId);
        Task<bool> DeleteCommentAsync(int commentId, int userId);
        Task<Collection> CreateCollectionAsync(Collection collection);
        Task<IEnumerable<Collection>> GetUserCollectionsAsync(int userId);
        Task<Collection?> GetCollectionByIdAsync(int collectionId, int userId);
        Task<bool> UpdateCollectionAsync(Collection collection);
        Task<bool> DeleteCollectionAsync(int collectionId, int userId);
        Task<bool> AddMemoryToCollectionAsync(int collectionId, int memoryId);
        Task<bool> RemoveMemoryFromCollectionAsync(int collectionId, int memoryId);
        Task<IEnumerable<UserMemory>> GetMemoriesInCollectionAsync(int collectionId);
        Task<Gift> CreateGiftAsync(Gift gift);
        Task<IEnumerable<Gift>> GetSentGiftsAsync(int userId);
        Task<IEnumerable<Gift>> GetReceivedGiftsAsync(string email);
        Task<Gift?> GetGiftByIdAsync(int giftId);
        Task<bool> MarkGiftAsOpenedAsync(int giftId);
    }
}
