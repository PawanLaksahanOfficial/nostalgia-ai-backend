using System.Net;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class PixabayStockPhotoProviderTests
    {
        private const string SearchJson = """
            {"total": 4692, "totalHits": 500, "hits": [
              {"id": 195893, "user": "Josch13", "webformatURL": "https://pixabay.com/get/35bbf209e13e39d2_640.jpg", "largeImageURL": "https://pixabay.com/get/ed6a99fd0a76647_1280.jpg"},
              {"id": 195894, "user": "Ana Silva", "webformatURL": "https://pixabay.com/get/aa_640.jpg"},
              {"id": 195895, "user": "No Image Links"}
            ]}
            """;

        // Answers searches with SearchJson and every other request (an image download) with the given status.
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _downloadStatus;
            public List<Uri> Requests { get; } = new();
            public int Searches => Requests.Count(uri => uri.AbsolutePath == "/api/");

            public StubHandler(HttpStatusCode downloadStatus = HttpStatusCode.OK) => _downloadStatus = downloadStatus;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri!);
                var isSearch = request.RequestUri!.AbsolutePath == "/api/";
                return Task.FromResult(new HttpResponseMessage(isSearch ? HttpStatusCode.OK : _downloadStatus)
                {
                    Content = new StringContent(isSearch ? SearchJson : string.Empty)
                });
            }
        }

        private sealed class SingleClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private static PixabayStockPhotoProvider NewProvider(StubHandler handler, string apiKey = "test-key") =>
            new(new SingleClientFactory(handler),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Pixabay:ApiKey"] = apiKey
                }).Build(),
                NullLogger<PixabayStockPhotoProvider>.Instance);

        [Fact]
        public void Reads_large_image_urls_and_uploaders_from_a_search()
        {
            var photos = PixabayStockPhotoProvider.ParseSearchResponse(SearchJson);

            Assert.Equal(2, photos.Count);
            Assert.Equal("195893", photos[0].Id);
            Assert.Equal("https://pixabay.com/get/ed6a99fd0a76647_1280.jpg", photos[0].DownloadUrl);
            Assert.Equal("Josch13", photos[0].PhotographerName);
            // Without largeImageURL, the 640px image is better than none.
            Assert.Equal("https://pixabay.com/get/aa_640.jpg", photos[1].DownloadUrl);
        }

        [Theory]
        [InlineData("{\"total\": 0, \"totalHits\": 0, \"hits\": []}")]
        [InlineData("{\"error\": \"nope\"}")]
        [InlineData("[]")]
        public void A_reply_without_hits_yields_nothing(string json)
        {
            Assert.Empty(PixabayStockPhotoProvider.ParseSearchResponse(json));
        }

        [Fact]
        public async Task Searches_for_safe_landscape_photos_with_the_key()
        {
            var handler = new StubHandler();

            await NewProvider(handler).SearchAsync("rain on window", 2);

            var query = handler.Requests.Single().Query;
            Assert.Contains("key=test-key", query);
            Assert.Contains("q=rain%20on%20window", query);
            Assert.Contains("image_type=photo", query);
            Assert.Contains("orientation=horizontal", query);
            Assert.Contains("safesearch=true", query);
            // Pixabay rejects fewer than 3 results per page.
            Assert.Contains("per_page=3", query);
        }

        [Fact]
        public async Task Answers_a_repeated_search_from_the_cache()
        {
            var handler = new StubHandler();
            var provider = NewProvider(handler);

            await provider.SearchAsync("rain on window", 3);
            var photos = await provider.SearchAsync("  Rain on Window ", 3);

            Assert.Equal(1, handler.Searches);
            Assert.Equal(2, photos.Count);
        }

        [Fact]
        public async Task Searches_again_once_a_cached_image_link_has_expired()
        {
            var handler = new StubHandler(downloadStatus: HttpStatusCode.Forbidden);
            var provider = NewProvider(handler);
            var photos = await provider.SearchAsync("rain on window", 3);

            var downloaded = await provider.DownloadAsync(photos[0], Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg"));
            await provider.SearchAsync("rain on window", 3);

            Assert.False(downloaded);
            Assert.Equal(2, handler.Searches);
        }

        [Fact]
        public async Task Does_not_call_pixabay_without_a_key()
        {
            var handler = new StubHandler();
            var provider = NewProvider(handler, apiKey: "");

            Assert.False(provider.IsEnabled);
            Assert.Empty(await provider.SearchAsync("rain on window", 3));
            Assert.Empty(handler.Requests);
        }
    }
}
