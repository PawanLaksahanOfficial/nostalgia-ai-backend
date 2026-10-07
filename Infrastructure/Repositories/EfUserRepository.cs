using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Services;
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
            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await _dbContext.Users
                .FirstOrDefaultAsync(x => x.Email == normalizedEmail && x.Active && !x.Deleted);
        }

        public async Task<User?> GetUserByEmailIncludingDeletedAsync(string email)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await _dbContext.Users
                .FirstOrDefaultAsync(x => x.Email == normalizedEmail);
        }

        public async Task<User?> GetUserByCanonicalEmailAsync(string email)
        {
            var canonicalEmail = EmailCanonicalizer.Canonicalize(email);
            return await _dbContext.Users
                .FirstOrDefaultAsync(u => u.CanonicalEmail == canonicalEmail && !u.Deleted);
        }

        public Task<int> CountUsersCreatedFromIpSinceAsync(string ipHash, DateTime since) =>
            _dbContext.Users.CountAsync(u => u.SignupIpHash == ipHash && u.CreatedDate >= since);

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

        public async Task<User?> RegisterUserAsync(RegisterRequest request, string passwordHash, string? signupIpHash)
        {
            var user = new User
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email.Trim().ToLowerInvariant(),
                CanonicalEmail = EmailCanonicalizer.Canonicalize(request.Email),
                EmailVerified = false,
                SignupIpHash = signupIpHash,
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
            // Whoever re-registers must prove they own the inbox, like a new sign-up.
            existingUser.EmailVerified = false;
            existingUser.CanonicalEmail = EmailCanonicalizer.Canonicalize(existingUser.Email);
            existingUser.Deleted = false;
            existingUser.Active = true;
            existingUser.LastUpdatedDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0 ? existingUser : null;
        }

        public async Task<bool> CreateEmailVerificationTokenAsync(EmailVerificationToken token)
        {
            _dbContext.EmailVerificationTokens.Add(token);
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
        }

        public async Task<bool> VerifyEmailAsync(string email, string token)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var record = await _dbContext.EmailVerificationTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.User.Email == normalizedEmail && t.Token == token && !t.User.Deleted);
            if (record == null)
            {
                return false;
            }
            if (record.User.EmailVerified)
            {
                // The same link opened twice.
                return true;
            }
            if (record.Used || record.ExpiresAt <= DateTime.UtcNow)
            {
                return false;
            }
            record.Used = true;
            record.User.EmailVerified = true;
            record.User.LastUpdatedDate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> MarkEmailVerifiedAsync(User user)
        {
            if (user.EmailVerified)
            {
                return true;
            }
            user.EmailVerified = true;
            user.LastUpdatedDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
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
            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await _dbContext.PasswordResetTokens
                .Include(prt => prt.User)
                .FirstOrDefaultAsync(prt =>
                    prt.User.Email == normalizedEmail &&
                    prt.Token == token &&
                    !prt.Used &&
                    prt.User.Active &&
                    !prt.User.Deleted &&
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

        public async Task<bool> ResetPasswordAsync(int userId, string passwordHash)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null) return false;
            var outstandingTokens = await _dbContext.PasswordResetTokens
                .Where(t => t.UserId == userId && !t.Used)
                .ToListAsync();

            user.PasswordHash = passwordHash;
            // The reset link was emailed, so using it proves the inbox is theirs.
            user.EmailVerified = true;
            user.LastUpdatedDate = DateTime.UtcNow;
            foreach (var token in outstandingTokens)
            {
                token.Used = true;
            }
            await _dbContext.SaveChangesAsync();
            return true;
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
                Email = model.Email.Trim().ToLowerInvariant(),
                CanonicalEmail = EmailCanonicalizer.Canonicalize(model.Email),
                // Google and Meta only hand over addresses they have verified.
                EmailVerified = true,
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

        public async Task<User?> ReactivateSocialLoginUserAsync(User existingUser, UserModel model)
        {
            existingUser.FirstName = model.FirstName;
            existingUser.LastName = model.LastName;
            existingUser.EmailVerified = true;
            existingUser.CanonicalEmail = EmailCanonicalizer.Canonicalize(existingUser.Email);
            existingUser.Deleted = false;
            existingUser.Active = true;
            existingUser.LastUpdatedDate = DateTime.UtcNow;
            existingUser.LastSignInDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0 ? existingUser : null;
        }

        public async Task<bool> SetAvatarUrlAsync(int userId, string? avatarUrl)
        {
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.Deleted);
            if (user == null) return false;
            user.AvatarUrl = avatarUrl;
            user.LastUpdatedDate = DateTime.UtcNow;
            var rowsAffected = await _dbContext.SaveChangesAsync();
            return rowsAffected > 0;
        }
    }
}
