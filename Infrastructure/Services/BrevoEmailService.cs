using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class BrevoEmailService : IEmailService
    {
        public const string HttpClientName = "Brevo";
        public const string SendEndpoint = "https://api.brevo.com/v3/smtp/email";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BrevoEmailService> _logger;
        private readonly string _apiKey;
        private readonly string _fromAddress;
        private readonly string _fromName;
        private readonly string _frontendBaseUrl;

        public BrevoEmailService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<BrevoEmailService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiKey = configuration["Brevo:ApiKey"] ?? string.Empty;
            _fromAddress = configuration["Email:FromAddress"] ?? string.Empty;
            _fromName = configuration["Email:FromName"] is { Length: > 0 } name ? name : "Nostalgia AI";
            _frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        }

        public async Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string userName)
        {
            var resetLink = EmailTemplates.PasswordResetLink(_frontendBaseUrl, resetToken, email);
            var content = EmailTemplates.PasswordReset(resetLink, userName);
            return await SendAsync(email, userName, content.Subject, content.HtmlBody, content.TextBody);
        }

        public Task<bool> SendEmailAsync(string to, string subject, string body) =>
            SendAsync(to, null, subject, body, null);

        private async Task<bool> SendAsync(string to, string? toName, string subject, string htmlBody, string? textBody)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_fromAddress))
            {
                _logger.LogError("Brevo email not sent: Brevo:ApiKey and Email:FromAddress must both be configured.");
                return false;
            }

            var payload = new BrevoEmail
            {
                Sender = new BrevoContact { Email = _fromAddress, Name = _fromName },
                To = new List<BrevoContact> { new() { Email = to, Name = string.IsNullOrWhiteSpace(toName) ? null : toName } },
                Subject = subject,
                HtmlContent = htmlBody,
                TextContent = textBody
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("api-key", _apiKey);
            request.Headers.Add("accept", "application/json");

            try
            {
                using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                var body = await response.Content.ReadAsStringAsync();
                // Typical causes: an invalid key, or a sender address not yet verified in Brevo.
                _logger.LogError("Brevo rejected email '{Subject}' with {StatusCode}: {Body}",
                    subject, (int)response.StatusCode, body.Length <= 500 ? body : body[..500]);
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "Could not reach Brevo to send email '{Subject}'.", subject);
                return false;
            }
        }

        private sealed class BrevoEmail
        {
            [JsonPropertyName("sender")] public BrevoContact Sender { get; init; } = new();
            [JsonPropertyName("to")] public List<BrevoContact> To { get; init; } = new();
            [JsonPropertyName("subject")] public string Subject { get; init; } = string.Empty;
            [JsonPropertyName("htmlContent")] public string HtmlContent { get; init; } = string.Empty;

            [JsonPropertyName("textContent")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? TextContent { get; init; }
        }

        private sealed class BrevoContact
        {
            [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;

            [JsonPropertyName("name")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Name { get; init; }
        }
    }
}
