using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace nostalgia_ai_backend.Tests.Repositories
{
    public class EfMemoryRepositoryTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<EFDbContext> _options;

        public EfMemoryRepositoryTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<EFDbContext>().UseSqlite(_connection).Options;
            using var context = new EFDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private async Task<(int UserId, int MemoryId)> SeedAsync()
        {
            await using var context = new EFDbContext(_options);
            var user = new User { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", Active = true };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            var memory = new UserMemory { UserId = user.UserId, Title = "Old title", StoryText = "Story", Status = VideoStatus.Processing };
            context.UserMemories.Add(memory);
            await context.SaveChangesAsync();
            return (user.UserId, memory.Id);
        }

        [Fact]
        public async Task Renaming_keeps_progress_the_worker_saved_after_the_row_was_loaded()
        {
            var (userId, memoryId) = await SeedAsync();

            await using var requestContext = new EFDbContext(_options);
            var repository = new EfMemoryRepository(requestContext);
            // The request handler loads the row while the video is still processing...
            var loaded = await repository.GetByIdForUserAsync(memoryId, userId);
            Assert.Equal(VideoStatus.Processing, loaded!.Status);

            // ...meanwhile the worker finishes the video in its own context.
            await using (var workerContext = new EFDbContext(_options))
            {
                var job = await workerContext.UserMemories.SingleAsync(m => m.Id == memoryId);
                job.Status = VideoStatus.Completed;
                job.FinalVideoPath = "memories/1/final.mp4";
                await workerContext.SaveChangesAsync();
            }

            Assert.True(await repository.RenameAsync(memoryId, userId, "New title"));

            await using var verifyContext = new EFDbContext(_options);
            var saved = await verifyContext.UserMemories.SingleAsync(m => m.Id == memoryId);
            Assert.Equal("New title", saved.Title);
            Assert.Equal(VideoStatus.Completed, saved.Status);
            Assert.Equal("memories/1/final.mp4", saved.FinalVideoPath);
        }

        [Fact]
        public async Task Renaming_someone_elses_video_changes_nothing()
        {
            var (userId, memoryId) = await SeedAsync();
            await using var context = new EFDbContext(_options);

            Assert.False(await new EfMemoryRepository(context).RenameAsync(memoryId, userId + 1, "Hijacked"));
            Assert.Equal("Old title", (await context.UserMemories.AsNoTracking().SingleAsync(m => m.Id == memoryId)).Title);
        }

        [Fact]
        public async Task Setting_public_touches_only_the_visibility_flag()
        {
            var (_, memoryId) = await SeedAsync();
            await using var context = new EFDbContext(_options);

            Assert.True(await new EfMemoryRepository(context).SetPublicAsync(memoryId, true));

            var saved = await context.UserMemories.AsNoTracking().SingleAsync(m => m.Id == memoryId);
            Assert.True(saved.IsPublic);
            Assert.Equal(VideoStatus.Processing, saved.Status);
        }
    }
}
