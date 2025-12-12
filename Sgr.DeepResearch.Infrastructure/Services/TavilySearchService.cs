using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sgr.DeepResearch.Core.Interfaces;

namespace Sgr.DeepResearch.Infrastructure.Services;

public class TavilySearchService : ISearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<TavilySearchService> _logger;

    public TavilySearchService(HttpClient httpClient, string apiKey, ILogger<TavilySearchService> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task<string> SearchAsync(string query, int maxResults = 5)
    {
        _logger.LogInformation("Tavily Search: {Query}", query);
        
        var requestBody = new
        {
            api_key = _apiKey,
            query = query,
            search_depth = "basic",
            include_answer = false,
            max_results = maxResults
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("https://api.tavily.com/search", content);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Tavily API error: {StatusCode}", response.StatusCode);
            return "Search failed.";
        }

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> ExtractContentAsync(List<string> urls)
    {
        // Упрощенная реализация - в реальном проекте используем endpoint extract Tavily или Firecrawl
        _logger.LogInformation("Tavily Extract: {Count} urls", urls.Count);
        return $"Simulated content for {urls.Count} URLs. In real implementation call Tavily Extract API.";
    }
}