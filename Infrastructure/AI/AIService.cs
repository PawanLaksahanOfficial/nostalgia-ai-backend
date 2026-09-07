using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Application.Exceptions;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Infrastructure.AI
{
    public class AIService : IAIService
    {
        private const string DefaultModel = "nvidia/nemotron-nano-12b-v2-vl:free";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AIService> _logger;

        public AIService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AIService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> GenerateNostalgicTextAsync(string prompt)
        {
            var section = _configuration.GetSection("OpenRouter");
            var url = section["Url"];
            var apiKey = section["ApiToken"];
            var model = section["Model"];
            if (string.IsNullOrWhiteSpace(model))
            {
                model = DefaultModel;
            }

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError("OpenRouter is not configured (Url or ApiToken missing).");
                throw new AIServiceException("The story generator is not configured.");
            }

            var payload = JsonConvert.SerializeObject(new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "You are a nostalgic storyteller." },
                    new { role = "user", content = prompt }
                }
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var httpClient = _httpClientFactory.CreateClient("OpenRouter");

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "OpenRouter request timed out after {Timeout}.", httpClient.Timeout);
                throw new AIServiceException("The story generator took too long to respond. Please try again.", ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "OpenRouter request failed.");
                throw new AIServiceException("Could not reach the story generator. Please try again.", ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenRouter returned {StatusCode}: {Body}",
                        (int)response.StatusCode, Truncate(body));

                    throw new AIServiceException(response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "The story generator is busy right now. Please try again in a moment."
                        : "The story generator could not complete your request. Please try again.");
                }

                var content = ExtractContent(body);
                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogError("OpenRouter returned an unusable payload: {Body}", Truncate(body));
                    throw new AIServiceException("The story generator returned an empty response. Please try again.");
                }
                return content;
            }
        }

        private static string? ExtractContent(string body)
        {
            try
            {
                return JObject.Parse(body)["choices"]?[0]?["message"]?["content"]?.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Truncate(string value, int maxLength = 500) =>
            value.Length <= maxLength ? value : value[..maxLength];
    }
}
