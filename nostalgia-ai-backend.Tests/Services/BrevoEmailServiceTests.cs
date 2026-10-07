using System.Net;
using System.Text.Json;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace nostalgia_ai_backend.Tests.Services
{
    public class BrevoEmailServiceTests
    {
        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            public HttpRequestMessage? Request { get; private set; }
            public string Body { get; private set; } = string.Empty;

            public RecordingHandler(HttpStatusCode status) => _status = status;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                Body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(_status) { Content = new StringContent("{\"message\":\"nope\"}") };
            }
        }

        private sealed class SingleClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private static BrevoEmailService NewService(RecordingHandler handler, string apiKey = "test-key", string from = "me@example.com") =>
            new(new SingleClientFactory(handler),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Brevo:ApiKey"] = apiKey,
                    ["Email:FromAddress"] = from,
                    ["Frontend:BaseUrl"] = "https://app.example.com/"
                }).Build(),
                NullLogger<BrevoEmailService>.Instance);

        [Fact]
        public async Task Posts_the_reset_email_to_brevo_with_the_api_key_and_reset_link()
        {
            var handler = new RecordingHandler(HttpStatusCode.Created);

            var sent = await NewService(handler).SendPasswordResetEmailAsync("jane@example.com", "abc123", "Jane Doe");

            Assert.True(sent);
            Assert.Equal(HttpMethod.Post, handler.Request!.Method);
            Assert.Equal(BrevoEmailService.SendEndpoint, handler.Request.RequestUri!.ToString());
            Assert.Equal("test-key", handler.Request.Headers.GetValues("api-key").Single());

            using var json = JsonDocument.Parse(handler.Body);
            var root = json.RootElement;
            Assert.Equal("me@example.com", root.GetProperty("sender").GetProperty("email").GetString());
            Assert.Equal("jane@example.com", root.GetProperty("to")[0].GetProperty("email").GetString());
            Assert.Contains("Reset", root.GetProperty("subject").GetString());
            Assert.Contains("https://app.example.com/reset-password?token=abc123&email=jane%40example.com",
                root.GetProperty("textContent").GetString());
        }

        [Fact]
        public async Task Returns_false_without_throwing_when_brevo_rejects_the_email()
        {
            var handler = new RecordingHandler(HttpStatusCode.BadRequest);

            Assert.False(await NewService(handler).SendEmailAsync("jane@example.com", "Hi", "<p>Hi</p>"));
        }

        [Theory]
        [InlineData("", "me@example.com")]
        [InlineData("test-key", "")]
        public async Task Does_not_call_brevo_until_the_key_and_sender_are_configured(string apiKey, string from)
        {
            var handler = new RecordingHandler(HttpStatusCode.Created);

            Assert.False(await NewService(handler, apiKey, from).SendEmailAsync("jane@example.com", "Hi", "<p>Hi</p>"));
            Assert.Null(handler.Request);
        }
    }
}
