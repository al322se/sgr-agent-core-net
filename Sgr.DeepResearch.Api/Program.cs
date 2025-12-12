using Microsoft.Extensions.Http.Resilience; // Подключаем Resilience
using Microsoft.SemanticKernel;
using Polly; // Для настройки стратегий
using Sgr.DeepResearch.Core.Interfaces;
using Sgr.DeepResearch.Infrastructure.Agents;
using Sgr.DeepResearch.Infrastructure.Http;
using Sgr.DeepResearch.Infrastructure.Persistence;
using Sgr.DeepResearch.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Читаем конфигурацию
var aiConfig = builder.Configuration.GetSection("AI");
var searchConfig = builder.Configuration.GetSection("Search");

// 2. Регистрация Semantic Kernel
// OpenAI SDK имеет встроенные ретраи, но мы создаем HttpClient вручную для StatusCodeFixHandler.
// Semantic Kernel управляет своим HttpClient сам, поэтому здесь оставляем как есть.
builder.Services.AddKernel()
    .AddOpenAIChatCompletion(
        modelId: aiConfig["ModelId"]!,
        apiKey: aiConfig["ApiKey"]!,
        httpClient: new HttpClient(new StatusCodeFixHandler(new HttpClientHandler())) 
        { 
            BaseAddress = new Uri(aiConfig["Endpoint"] ?? "https://api.openai.com/v1"),
            Timeout = TimeSpan.FromMinutes(5)
        }
    );

// 3. Регистрация инфраструктурных сервисов
builder.Services.AddSingleton<IResearchRepository, InMemoryResearchRepository>();

// --- РЕГИСТРАЦИЯ TAVILY С RESILIENCE (УСТОЙЧИВОСТЬЮ) ---
builder.Services.AddHttpClient<ISearchService, TavilySearchService>(client => 
{
    client.BaseAddress = new Uri("https://api.tavily.com");
    client.Timeout = TimeSpan.FromSeconds(30); // Общий таймаут на 1 попытку
})
.AddTypedClient<ISearchService>((http, sp) => 
    new TavilySearchService(
        http, 
        searchConfig["TavilyApiKey"]!, 
        sp.GetRequiredService<ILogger<TavilySearchService>>()
    ))
.AddStandardResilienceHandler(options => 
{
    // Настройка повторных попыток (Retries)
    options.Retry.MaxRetryAttempts = 3; // Пробуем 3 раза
    
    // Настройка задержки (Backoff)
    // Exponential означает: 2 сек -> 4 сек -> 8 сек.
    // Это лучше, чем ждать минуту, так как пользователь не будет ждать так долго.
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.Delay = TimeSpan.FromSeconds(2);
    
    // Настройка общего таймауда на все попытки (чтобы не висеть вечно)
    options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
});

// Регистрация движка агента
builder.Services.AddTransient<IAgentEngine, SgrAgentEngine>();

// 4. API и Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();
app.MapControllers();

app.Run();