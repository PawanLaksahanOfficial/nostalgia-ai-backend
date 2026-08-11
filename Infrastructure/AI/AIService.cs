using System.Text;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Infrastructure.AI
{
    public class AIService : IAIService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public AIService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<string> GenerateNostalgicTextAsync(string prompt, CancellationToken cancellationToken = default)
        {
            //var url = _configuration.GetSection("OpenRouter")["Url"];
            //var apiKey = _configuration.GetSection("OpenRouter")["ApiToken"];

            //var requestBody = new
            //{
            //    model = "nvidia/nemotron-nano-12b-v2-vl:free",
            //    messages = new[]
            //    {
            //        new { role = "system", content = "You are a nostalgic storyteller." },
            //        new { role = "user", content = prompt }
            //    }
            //};

            //var json = JsonConvert.SerializeObject(requestBody);
            //var httpClient = _httpClientFactory.CreateClient("OpenRouter");

            //// Use HttpRequestMessage to avoid modifying shared HttpClient DefaultRequestHeaders
            //using var request = new HttpRequestMessage(HttpMethod.Post, url);
            //request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            //request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            //// Pass cancellationToken into SendAsync and ReadAsStringAsync
            //using var response = await httpClient.SendAsync(request, cancellationToken);

            //if (response != null && response.IsSuccessStatusCode)
            //{
            //    var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            //    dynamic? responseObject = JsonConvert.DeserializeObject(responseString);
            //    return responseObject?.choices[0]?.message?.content ?? string.Empty;
            //}

            //return string.Empty;
            return await Task.FromResult("Mocked nostalgic response: The application is running successfully.");
        }
    }
}