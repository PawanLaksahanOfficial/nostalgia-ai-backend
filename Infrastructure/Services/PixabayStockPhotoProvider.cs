using System.Collections.Concurrent;
using System.Text.Json;
using Application.DTOs;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    // Pixabay's API terms require caching each search for 24 hours and downloading images rather than
    // hotlinking them. Registered as a singleton so the cache outlives each job's scope.
    public class PixabayStockPhotoProvider : IStockPhotoProvider
    {
        public const string HttpClientName = "Pixabay";
        private const string SearchEndpoint = "https://pixabay.com/api/";
        private const long MaxPhotoBytes = 15 * 1024 * 1024;
        private const int MaxQueryLength = 100;
        private const int MaxCachedSearches = 1000;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PixabayStockPhotoProvider> _logger;
        private readonly string _apiKey;
        private readonly ConcurrentDictionary<string, CachedSearch> _cache = new();

        private sealed record CachedSearch(DateTimeOffset FetchedAt, IReadOnlyList<StockPhoto> Photos);

        public PixabayStockPhotoProvider(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<PixabayStockPhotoProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiKey = configuration["Pixabay:ApiKey"] ?? string.Empty;
        }

        public string SourceName => "Pixabay";

        public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

        public async Task<IReadOnlyList<StockPhoto>> SearchAsync(
            string query, int count, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<StockPhoto>();
            }

            var term = query.Trim();
            if (term.Length > MaxQueryLength)
            {
                term = term[..MaxQueryLength];
            }
            var perPage = Math.Clamp(count, 3, 200);
            var cacheKey = $"{term.ToLowerInvariant()}|{perPage}";
            if (_cache.TryGetValue(cacheKey, out var cached) &&
                DateTimeOffset.UtcNow - cached.FetchedAt < CacheDuration)
            {
                return cached.Photos;
            }

            var url = $"{SearchEndpoint}?key={Uri.EscapeDataString(_apiKey)}&q={Uri.EscapeDataString(term)}" +
                      $"&image_type=photo&orientation=horizontal&safesearch=true&per_page={perPage}";
            try
            {
                using var response = await _httpClientFactory.CreateClient(HttpClientName)
                    .GetAsync(url, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    // Errors come back as plain text, e.g. "[ERROR 400] Invalid or missing API key".
                    // The URL is never logged: it carries the key.
                    _logger.LogWarning("Pixabay search for '{Query}' returned {StatusCode}: {Body}",
                        term, (int)response.StatusCode, body.Length <= 300 ? body : body[..300]);
                    return Array.Empty<StockPhoto>();
                }
                var photos = ParseSearchResponse(body);
                Remember(cacheKey, photos);
                return photos;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "Pixabay search for '{Query}' failed.", term);
                return Array.Empty<StockPhoto>();
            }
        }

        public static IReadOnlyList<StockPhoto> ParseSearchResponse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("hits", out var hits) ||
                hits.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<StockPhoto>();
            }

            var results = new List<StockPhoto>();
            foreach (var hit in hits.EnumerateArray())
            {
                if (hit.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                // largeImageURL is at most 1280px; the 640px webformatURL is a fallback.
                var downloadUrl = ReadString(hit, "largeImageURL") ?? ReadString(hit, "webformatURL");
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    continue;
                }
                results.Add(new StockPhoto
                {
                    Id = hit.TryGetProperty("id", out var id) ? id.ToString() : downloadUrl,
                    DownloadUrl = downloadUrl,
                    PhotographerName = ReadString(hit, "user") ?? string.Empty
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
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Pixabay photo {PhotoId} download returned {StatusCode}.",
                        photo.Id, (int)response.StatusCode);
                    // Pixabay's image links expire, so the cached search that produced this one is stale.
                    Forget(photo.DownloadUrl);
                    return false;
                }
                if (response.Content.Headers.ContentLength is > MaxPhotoBytes)
                {
                    _logger.LogWarning("Pixabay photo {PhotoId} is too large; skipping it.", photo.Id);
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
                _logger.LogWarning("Pixabay photo {PhotoId} is not a supported image; skipping it.", photo.Id);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                _logger.LogWarning(ex, "Could not download Pixabay photo {PhotoId}.", photo.Id);
            }

            TryDelete(destinationPath);
            return false;
        }

        private void Remember(string cacheKey, IReadOnlyList<StockPhoto> photos)
        {
            var now = DateTimeOffset.UtcNow;
            if (_cache.Count >= MaxCachedSearches)
            {
                foreach (var entry in _cache)
                {
                    if (now - entry.Value.FetchedAt >= CacheDuration)
                    {
                        _cache.TryRemove(entry.Key, out _);
                    }
                }
                if (_cache.Count >= MaxCachedSearches)
                {
                    var oldest = _cache.OrderBy(entry => entry.Value.FetchedAt).Select(entry => entry.Key).FirstOrDefault();
                    if (oldest != null)
                    {
                        _cache.TryRemove(oldest, out _);
                    }
                }
            }
            _cache[cacheKey] = new CachedSearch(now, photos);
        }

        private void Forget(string downloadUrl)
        {
            foreach (var entry in _cache)
            {
                if (entry.Value.Photos.Any(photo => photo.DownloadUrl == downloadUrl))
                {
                    _cache.TryRemove(entry.Key, out _);
                }
            }
        }

        private static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

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
