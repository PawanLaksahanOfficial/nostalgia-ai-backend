using Application.DTOs;
using Domain.Entities;

namespace Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByEmailIncludingDeletedAsync(string email);
        Task<bool> UpdateUserLoginStatusAsync(User user);
        Task<User?> RegisterUserAsync(RegisterRequest request, string passwordHash);
        Task<User?> ReactivateDeletedUserAsync(User existingUser, RegisterRequest request, string passwordHash);
        Task<bool> UpdatePasswordAsync(int userId, string passwordHash);
        Task<bool> CreatePasswordResetTokenAsync(PasswordResetToken resetToken);
        Task<PasswordResetToken?> ValidatePasswordResetTokenAsync(string email, string token);
        Task<bool> MarkResetTokenAsUsedAsync(int tokenId);
        Task<User?> GetByIdAsync(int userId);
        Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request);
        Task<User?> CreateSocialLoginUserAsync(UserModel model);
        Task<User?> ReactivateSocialLoginUserAsync(User existingUser, UserModel model);
    }
}