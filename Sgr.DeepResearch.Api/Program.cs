using Microsoft.SemanticKernel;
using Sgr.DeepResearch.Core.Interfaces;
using Sgr.DeepResearch.Infrastructure.Agents;
using Sgr.DeepResearch.Infrastructure.Http; // Добавили namespace
using Sgr.DeepResearch.Infrastructure.Persistence;
using Sgr.DeepResearch.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Читаем конфигурацию
var aiConfig = builder.Configuration.GetSection("AI");
var searchConfig = builder.Configuration.GetSection("Search");

// 2. Настройка HttpClient для AI
// Мы не регистрируем его в DI стандартным способом, так как SK требует инстанс.
// Но мы можем зарегистрировать хендлер, если захотим использовать Factory, 
// однако для простоты создадим цепочку вручную ниже.

// 3. Регистрация Semantic Kernel с StatusCodeFixHandler
builder.Services.AddKernel()
    .AddOpenAIChatCompletion(
        modelId: aiConfig["ModelId"]!,
        apiKey: aiConfig["ApiKey"]!,
        // ВАЖНО: Создаем HttpClient с нашим хендлером
        httpClient: new HttpClient(new StatusCodeFixHandler(new HttpClientHandler())) 
        { 
            // Устанавливаем BaseAddress для PolzaAI / OpenRouter
            BaseAddress = new Uri(aiConfig["Endpoint"] ?? "https://api.openai.com/v1"),
            // Увеличиваем таймаут, так как Deep Research может думать долго
            Timeout = TimeSpan.FromMinutes(5)
        }
    );

// 4. Регистрация инфраструктурных сервисов
builder.Services.AddSingleton<IResearchRepository, InMemoryResearchRepository>();

// Регистрация Tavily (Поиск)
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

// Регистрация движка агента
builder.Services.AddTransient<IAgentEngine, SgrAgentEngine>();

// 5. API и Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();
app.MapControllers();

app.Run();