using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Storage
{
    public class LocalFileStorage : IFileStorage
    {
        private readonly string _storagePath;
        private readonly string _baseUrl;

        public LocalFileStorage(IConfiguration configuration)
        {
            _storagePath = configuration.GetValue<string>("Storage:LocalPath") ?? Path.Combine(Directory.GetCurrentDirectory(), "storage");
            _baseUrl = configuration.GetValue<string>("Storage:BaseUrl") ?? "/storage";
            
            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }

        public async Task<string> UploadAsync(string key, Stream content, string contentType)
        {
            var filePath = GetFullPath(key);
            var directory = Path.GetDirectoryName(filePath);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(fileStream);

            return GetPublicUrl(key);
        }

        public Task<Stream?> DownloadAsync(string key)
        {
            var filePath = GetFullPath(key);
            
            if (!File.Exists(filePath))
            {
                return Task.FromResult<Stream?>(null);
            }

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            return Task.FromResult<Stream?>(stream);
        }

        public Task<bool> DeleteAsync(string key)
        {
            var filePath = GetFullPath(key);
            
            if (!File.Exists(filePath))
            {
                return Task.FromResult(false);
            }

            File.Delete(filePath);
            return Task.FromResult(true);
        }

        public string GetPublicUrl(string key)
        {
            return $"{_baseUrl}/{key.Replace('\\', '/')}";
        }

        private string GetFullPath(string key)
        {
            var sanitizedKey = key.Replace("..", "").TrimStart('/').TrimStart('\\');
            return Path.Combine(_storagePath, sanitizedKey);
        }
    }
}