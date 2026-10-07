using System.Text.Json;
using Application.DTOs;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class PexelsStockPhotoProvider : IStockPhotoProvider
    {
        public const string HttpClientName = "Pexels";
        private const string SearchEndpoint = "https://api.pexels.com/v1/search";
        private const long MaxPhotoBytes = 15 * 1024 * 1024;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PexelsStockPhotoProvider> _logger;
        private readonly string _apiKey;

        public PexelsStockPhotoProvider(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<PexelsStockPhotoProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiKey = configuration["Pexels:ApiKey"] ?? string.Empty;
        }

        public string SourceName => "Pexels";

        public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<IReadOnlyList<StockPhoto>> SearchAsync(
            string query, int count, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<StockPhoto>();
            }

            var url = $"{SearchEndpoint}?query={Uri.EscapeDataString(query.Trim())}" +
                      $"&per_page={Math.Clamp(count, 1, 15)}&orientation=landscape";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", _apiKey);

            try
            {
                using var response = await _httpClientFactory.CreateClient(HttpClientName)
                    .SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Pexels search for '{Query}' returned {StatusCode}: {Body}",
                        query, (int)response.StatusCode, body.Length <= 300 ? body : body[..300]);
                    return Array.Empty<StockPhoto>();
                }
                return ParseSearchResponse(body);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "Pexels search for '{Query}' failed.", query);
                return Array.Empty<StockPhoto>();
            }
        }

        public static IReadOnlyList<StockPhoto> ParseSearchResponse(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("photos", out var photos) ||
                photos.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<StockPhoto>();
            }

            var results = new List<StockPhoto>();
            foreach (var photo in photos.EnumerateArray())
            {
                if (!photo.TryGetProperty("src", out var src) ||
                    !src.TryGetProperty("large2x", out var large) ||
                    large.GetString() is not { Length: > 0 } downloadUrl)
                {
                    continue;
                }
                results.Add(new StockPhoto
                {
                    Id = photo.TryGetProperty("id", out var id) ? id.ToString() : downloadUrl,
                    DownloadUrl = downloadUrl,
                    PhotographerName = photo.TryGetProperty("photographer", out var name)
                        ? name.GetString() ?? string.Empty
                        : string.Empty
                });
            }
            return results;
        }

        public async Task<bool> DownloadAsync(
            StockPhoto photo, string destinationPath, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClientFactory.CreateClient(HttpClientName)
                    .GetAsync(photo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentLength is > MaxPhotoBytes)
                {
                    _logger.LogWarning("Pexels photo {PhotoId} download returned {StatusCode}.",
                        photo.Id, (int)response.StatusCode);
                    return false;
                }

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var destination = File.Create(destinationPath))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                bool isImage;
                await using (var check = File.OpenRead(destinationPath))
                {
                    isImage = ImageValidator.TryResolve(check, out _, out _);
                }
                if (isImage)
                {
                    return true;
                }
                _logger.LogWarning("Pexels photo {PhotoId} is not a supported image; skipping it.", photo.Id);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                _logger.LogWarning(ex, "Could not download Pexels photo {PhotoId}.", photo.Id);
            }

            TryDelete(destinationPath);
            return false;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
