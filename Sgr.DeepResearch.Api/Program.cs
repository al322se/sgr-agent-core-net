// Sgr.DeepResearch.Api/Program.cs
using Microsoft.SemanticKernel;
using Sgr.DeepResearch.Core.Interfaces;
using Sgr.DeepResearch.Infrastructure.Agents;
using Sgr.DeepResearch.Infrastructure.Persistence;
using Sgr.DeepResearch.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Конфигурация
var aiConfig = builder.Configuration.GetSection("AI");
var searchConfig = builder.Configuration.GetSection("Search");

// 2. Настройка HttpClient для AI (нужен для подмены BaseUrl)
// Мы создаем именованный клиент, чтобы при желании добавить туда хендлеры (как в TelegramBotAI)
builder.Services.AddHttpClient("AiClient", client =>
{
    // Важно: Semantic Kernel требует, чтобы BaseAddress был установлен, если мы не используем дефолтный OpenAI
    // Если в конфиге есть Endpoint (PolzaAI/OpenRouter), ставим его
    if (!string.IsNullOrEmpty(aiConfig["Endpoint"]))
    {
        client.BaseAddress = new Uri(aiConfig["Endpoint"]!);
    }
    client.Timeout = TimeSpan.FromMinutes(5);
});

// 3. Регистрация Semantic Kernel
// ИСПРАВЛЕНИЕ: Сначала вызываем AddKernel(), затем AddOpenAIChatCompletion
builder.Services.AddKernel()
    .AddOpenAIChatCompletion(
        modelId: aiConfig["ModelId"]!,
        apiKey: aiConfig["ApiKey"]!,
        // Здесь мы используем Factory для получения HttpClient, который настроили выше
        httpClient: new HttpClient 
        { 
            // Хак для совместимости: если используем Polza/OpenRouter, 
            // иногда нужно передать BaseAddress явно через этот HttpClient, 
            // если стандартный коннектор его не подхватывает.
            BaseAddress = !string.IsNullOrEmpty(aiConfig["Endpoint"]) 
                ? new Uri(aiConfig["Endpoint"]!) 
                : null
        }
    );

// 4. Регистрация сервисов приложения
builder.Services.AddSingleton<IResearchRepository, InMemoryResearchRepository>();

// Регистрируем TavilySearchService и типизированный HttpClient для него
builder.Services.AddHttpClient<ISearchService, TavilySearchService>(client => 
{
    client.BaseAddress = new Uri("https://api.tavily.com");
})
.AddTypedClient<ISearchService>((http, sp) => 
    new TavilySearchService(
        http, 
        searchConfig["TavilyApiKey"]!, 
        sp.GetRequiredService<ILogger<TavilySearchService>>()
    ));

builder.Services.AddTransient<IAgentEngine, SgrAgentEngine>();

// 5. API Конфигурация
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();