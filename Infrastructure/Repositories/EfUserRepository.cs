using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            var user = await _dbContext.Users.FirstOrDefaultAsync((x => x.Email == email && x.Active && !x.Deleted));
            if (user != null) 
            {
                return user;
            }
            return null;
        }

        public async Task<bool> UpdateUserLoginStatusAsync(User user)
        {
            user.LastSignInDate = DateTime.UtcNow;
            user.LastUpdatedDate = DateTime.UtcNow;
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }

        public async Task<bool> CreateNewUserAsync(UserModel model)
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
                Deleted = false
            };
            _dbContext.Users.Add(user);
            var rows = await _dbContext.SaveChangesAsync();
            return rows > 0;
        }
    }
}
