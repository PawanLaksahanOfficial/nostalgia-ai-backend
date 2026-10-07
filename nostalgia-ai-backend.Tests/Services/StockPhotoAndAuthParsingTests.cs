using Infrastructure.Services;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class StockPhotoAndAuthParsingTests
    {
        [Fact]
        public void Reads_large_photo_urls_and_photographers_from_a_pexels_search()
        {
            const string json = """
                {"photos": [
                  {"id": 101, "photographer": "Ana Silva", "src": {"large2x": "https://images.pexels.com/101.jpeg", "small": "x"}},
                  {"id": 102, "photographer": "Ravi Perera", "src": {"medium": "no-large2x"}},
                  {"id": 103, "src": {"large2x": "https://images.pexels.com/103.jpeg"}}
                ]}
                """;

            var photos = PexelsStockPhotoProvider.ParseSearchResponse(json);

            Assert.Equal(2, photos.Count);
            Assert.Equal("101", photos[0].Id);
            Assert.Equal("https://images.pexels.com/101.jpeg", photos[0].DownloadUrl);
            Assert.Equal("Ana Silva", photos[0].PhotographerName);
            Assert.Equal(string.Empty, photos[1].PhotographerName);
        }

        [Fact]
        public void A_pexels_reply_without_photos_yields_nothing()
        {
            Assert.Empty(PexelsStockPhotoProvider.ParseSearchResponse("{\"error\": \"Rate limit\"}"));
        }

        [Fact]
        public void Google_client_id_setting_accepts_a_comma_separated_list()
        {
            var ids = AuthenticationService.ParseGoogleClientIds(" dev.apps.googleusercontent.com , prod.apps.googleusercontent.com,dev.apps.googleusercontent.com ");

            Assert.Equal(new[] { "dev.apps.googleusercontent.com", "prod.apps.googleusercontent.com" }, ids);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , ")]
        public void A_missing_google_client_id_yields_no_audience(string? configured)
        {
            Assert.Empty(AuthenticationService.ParseGoogleClientIds(configured));
        }
    }
}
