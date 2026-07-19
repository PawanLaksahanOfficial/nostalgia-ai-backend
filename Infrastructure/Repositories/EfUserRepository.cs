using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class EfUserRepository : IUserRepository
    {
        private readonly EFDbContext _dbContext;

        public EfUserRepository(EFDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(x => x.Email == email && x.Active && !x.Deleted);
        }

        public async Task<User?> GetUserByEmailIncludingDeletedAsync(string email)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(x => x.Email == email);
        }

        public async Task<User?> GetByIdAsync(int userId)
        {
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
        }

        public async Task<bool> UpdateUserLoginStatusAsync(User user)
        {
            user.LastSignInDate = DateTime.UtcNow;
            user.LastUpdatedDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
        }

        public async Task<User?> RegisterUserAsync(RegisterRequest request, string passwordHash)
        {
            var user = new User
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                PasswordHash = passwordHash,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                LastSignInDate = DateTime.UtcNow,
                Active = true,
                Deleted = false,
                Tier = UserTier.Free,
                MonthlyMemoryCount = 0,
                MonthlyCountResetDate = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0 ? user : null;
        }

        public async Task<User?> ReactivateDeletedUserAsync(User existingUser, RegisterRequest request, string passwordHash)
        {
            existingUser.FirstName = request.FirstName;
            existingUser.LastName = request.LastName;
            existingUser.PasswordHash = passwordHash;
            existingUser.Deleted = false;
            existingUser.Active = true;
            existingUser.LastUpdatedDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0 ? existingUser : null;
        }

        public async Task<bool> UpdatePasswordAsync(int userId, string passwordHash)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null) return false;
            user.PasswordHash = passwordHash;
            user.LastUpdatedDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
        }

        public async Task<bool> CreatePasswordResetTokenAsync(PasswordResetToken resetToken)
        {
            _dbContext.PasswordResetTokens.Add(resetToken);
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
        }

        public async Task<PasswordResetToken?> ValidatePasswordResetTokenAsync(string email, string token)
        {
            return await _dbContext.PasswordResetTokens
                .Include(prt => prt.User)
                .FirstOrDefaultAsync(prt =>
                    prt.User.Email == email &&
                    prt.Token == token &&
                    !prt.Used &&
                    prt.ExpiresAt > DateTime.UtcNow);
        }

        public async Task<bool> MarkResetTokenAsUsedAsync(int tokenId)
        {
            var token = await _dbContext.PasswordResetTokens.FindAsync(tokenId);
            if (token == null) return false;
            token.Used = true;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
        }

        public async Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null) return false;
            if (!string.IsNullOrEmpty(request.FirstName))
            {
                user.FirstName = request.FirstName;
            }
            if (!string.IsNullOrEmpty(request.LastName))
            {
                user.LastName = request.LastName;
            }
            if (request.AvatarUrl != null)
            {
                user.AvatarUrl = request.AvatarUrl;
            }
            user.LastUpdatedDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
        }

        public async Task<User?> CreateSocialLoginUserAsync(UserModel model)
        {
            var user = new User
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                LastSignInDate = DateTime.UtcNow,
                Active = true,
                Deleted = false,
                Tier = UserTier.Free,
                MonthlyMemoryCount = 0,
                MonthlyCountResetDate = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0 ? user : null;
        }
    }
}
