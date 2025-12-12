using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // Нужно для правильной сериализации request body
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
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Tavily Search API error: {StatusCode}. Body: {Body}", response.StatusCode, error);
            return $"Search failed: {response.StatusCode}";
        }

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> ExtractContentAsync(List<string> urls)
    {
        _logger.LogInformation("Tavily Extract: {Count} urls", urls.Count);

        // Формируем запрос к эндпоинту /extract
        // Документация: https://docs.tavily.com/docs/tavily-api/rest-api#extract
        var requestBody = new
        {
            api_key = _apiKey,
            urls = urls // Список URL для извлечения
        };

        var jsonOptions = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower 
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");
        
        // ВАЖНО: Используем правильный эндпоинт
        var response = await _httpClient.PostAsync("https://api.tavily.com/extract", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Tavily Extract API error: {StatusCode}. Body: {Body}", response.StatusCode, error);
            return $"Extract failed: {response.StatusCode} - {error}";
        }

        var resultJson = await response.Content.ReadAsStringAsync();
        
        // Для экономии токенов и читаемости лога можно немного почистить результат (опционально),
        // но агент просил raw content, так что отдаем как есть.
        return resultJson;
    }
}