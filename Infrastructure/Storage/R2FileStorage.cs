using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Storage
{
    public class R2FileStorage : IFileStorage
    {
        private readonly IAmazonS3 _s3;
        private readonly ILogger<R2FileStorage> _logger;
        private readonly string _bucket;
        private readonly string _publicBaseUrl;

        public R2FileStorage(IConfiguration configuration, ILogger<R2FileStorage> logger)
        {
            _logger = logger;
            var section = configuration.GetSection("Storage");
            var accountId = section["AccountId"] ?? string.Empty;
            var accessKey = section["AccessKey"] ?? string.Empty;
            var secretKey = section["SecretKey"] ?? string.Empty;
            _bucket = section["Bucket"] ?? string.Empty;
            _publicBaseUrl = (section["PublicBaseUrl"] ?? string.Empty).TrimEnd('/');

            if (string.IsNullOrWhiteSpace(accountId) ||
                string.IsNullOrWhiteSpace(accessKey) ||
                string.IsNullOrWhiteSpace(secretKey) ||
                string.IsNullOrWhiteSpace(_bucket))
            {
                throw new InvalidOperationException(
                    "Storage:Provider is 'r2' but AccountId, AccessKey, SecretKey or Bucket is missing.");
            }
            _s3 = new AmazonS3Client(
                new BasicAWSCredentials(accessKey, secretKey),
                new AmazonS3Config
                {
                    ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
                    AuthenticationRegion = "auto",
                    ForcePathStyle = true
                });
        }

        public async Task<string> UploadAsync(string key, Stream content, string contentType)
        {
            var normalized = Normalize(key);
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = normalized,
                InputStream = content,
                ContentType = contentType,
                DisablePayloadSigning = true
            });
            return GetPublicUrl(normalized);
        }

        public async Task<Stream?> DownloadAsync(string key)
        {
            try
            {
                var response = await _s3.GetObjectAsync(_bucket, Normalize(key));
                var buffer = new MemoryStream();
                await response.ResponseStream.CopyToAsync(buffer);
                buffer.Position = 0;
                return buffer;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                await _s3.DeleteObjectAsync(_bucket, Normalize(key));
                return true;
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete '{Key}' from R2.", key);
                return false;
            }
        }

        public string GetPublicUrl(string key)
        {
            var normalized = Normalize(key);
            return string.IsNullOrEmpty(_publicBaseUrl)
                ? normalized
                : $"{_publicBaseUrl}/{normalized}";
        }

        private static string Normalize(string key) =>
            key.Replace('\\', '/').TrimStart('/');
    }
}
