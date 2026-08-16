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

        public async Task<string> GenerateNostalgicTextAsync(string prompt)
        {
            var url = _configuration.GetSection("OpenRouter")["Url"];
            var apiKey = _configuration.GetSection("OpenRouter")["ApiToken"]; 
            var requestBody = new
            {
                model = "nvidia/nemotron-nano-12b-v2-vl:free",
                messages = new[]
                {
                    new { role = "system", content = "You are a nostalgic storyteller." },
                    new { role = "user", content = prompt }
                }
            };
            
            var json = JsonConvert.SerializeObject(requestBody);
            var httpClient = _httpClientFactory.CreateClient("OpenRouter");
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            var response = await httpClient.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var message = string.Empty;
            
            if (response != null)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                dynamic? responseObject = JsonConvert.DeserializeObject(responseString);
                message = responseObject?.choices[0]?.message?.content ?? string.Empty;
            }
            
            return message;
        }
    }
}