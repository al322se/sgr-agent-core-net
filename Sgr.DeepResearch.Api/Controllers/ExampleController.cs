using Microsoft.AspNetCore.Mvc;
using Sgr.DeepResearch.Core.Interfaces;
using Sgr.DeepResearch.Core.Models;

namespace Sgr.DeepResearch.Api.Controllers;

[ApiController]
[Route("api/example")]
public class ExampleController : ControllerBase
{
    private readonly IAgentEngine _agentEngine;
    private readonly IResearchRepository _repository;
    private readonly ILogger<ExampleController> _logger;

    public ExampleController(
        IAgentEngine agentEngine, 
        IResearchRepository repository,
        ILogger<ExampleController> logger)
    {
        _agentEngine = agentEngine;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Запускает демо-исследование про BMW X6 2025 в России.
    /// Аналог Python скрипта: client.chat.completions.create(...)
    /// </summary>
    [HttpPost("bmw-search")]
    public async Task<IActionResult> RunBmwDemo()
    {
        // 1. Формируем запрос (как в Python примере)
        string prompt = "Research BMW X6 2025 prices in Russia. Find official dealers and parallel import offers.";
        
        _logger.LogInformation("Starting Demo Research: {Prompt}", prompt);

        // 2. Создаем состояние в базе (в памяти)
        var state = await _repository.CreateAsync(prompt);

        // 3. Запускаем агента в фоне (Fire-and-forget)
        // В реальном приложении здесь лучше использовать Hangfire или BackgroundService + Channel
        _ = Task.Run(async () => 
        {
            try 
            {
                await _agentEngine.RunFullLoopAsync(state.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background demo failed");
            }
        });

        // 4. Возвращаем ID, чтобы пользователь мог проверить результат через GET /api/research/{id}
        return Accepted(new 
        { 
            Message = "Research started successfully", 
            ResearchId = state.Id,
            Task = prompt,
            CheckStatusUrl = $"/api/research/{state.Id}"
        });
    }
}