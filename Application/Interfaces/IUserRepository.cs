using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByEmailIncludingDeletedAsync(string email);
        Task<bool> UpdateUserLoginStatusAsync(User user);
        Task<bool> CreateNewUserAsync(UserModel model);
        Task<bool> UpdatePasswordAsync(int userId, string passwordHash);
        Task<int> CreatePasswordResetTokenAsync(PasswordResetToken resetToken);
        Task<PasswordResetToken?> ValidatePasswordResetTokenAsync(string email, string token);
        Task<bool> MarkResetTokenAsUsedAsync(int tokenId);
        Task<User?> GetByIdAsync(int userId);
        Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request);
    }
}