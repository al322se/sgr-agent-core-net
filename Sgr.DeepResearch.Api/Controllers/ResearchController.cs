using Microsoft.AspNetCore.Mvc;
using Sgr.DeepResearch.Core.Interfaces;
using Sgr.DeepResearch.Core.Models;

namespace Sgr.DeepResearch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResearchController : ControllerBase
{
    private readonly IAgentEngine _agentEngine;
    private readonly IResearchRepository _repository;

    public ResearchController(IAgentEngine agentEngine, IResearchRepository repository)
    {
        _agentEngine = agentEngine;
        _repository = repository;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartResearch([FromBody] StartRequest request)
    {
        var state = await _repository.CreateAsync(request.Task);
        
        // Запускаем в фоне (Fire and forget для примера, в проде лучше через Hangfire/Queue)
        _ = Task.Run(() => _agentEngine.RunFullLoopAsync(state.Id));

        return Ok(new { ResearchId = state.Id, Status = "Started" });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetStatus(Guid id)
    {
        var state = await _repository.GetAsync(id);
        if (state == null) return NotFound();
        return Ok(state);
    }
}

public class StartRequest
{
    public string Task { get; set; } = string.Empty;
}