using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Sgr.DeepResearch.Core.Interfaces;
using Sgr.DeepResearch.Core.Models;

namespace Sgr.DeepResearch.Infrastructure.Agents;

public class SgrAgentEngine : IAgentEngine
{
    private readonly IResearchRepository _repository;
    private readonly ISearchService _searchService;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<SgrAgentEngine> _logger;

    // Константы конфигурации
    private const string ModelId = "gpt-4o-mini"; // Или ваша модель в OpenRouter
    private const int MaxIterations = 10;

    public SgrAgentEngine(
        IResearchRepository repository,
        ISearchService searchService,
        Kernel kernel, // SK Kernel инжектится сюда
        ILogger<SgrAgentEngine> logger)
    {
        _repository = repository;
        _searchService = searchService;
        _chatService = kernel.GetRequiredService<IChatCompletionService>();
        _logger = logger;
    }

    public async Task RunFullLoopAsync(Guid researchId, CancellationToken ct = default)
    {
        var state = await _repository.GetAsync(researchId);
        if (state == null) throw new KeyNotFoundException($"Research {researchId} not found");

        state.Status = ResearchStatus.Running;
        await _repository.UpdateAsync(state);

        try
        {
            while (state.Status == ResearchStatus.Running && state.IterationCount < MaxIterations && !ct.IsCancellationRequested)
            {
                await RunIterationAsync(researchId);
                // Перечитываем состояние, так как оно обновилось
                state = await _repository.GetAsync(researchId);
                
                if (state!.Status == ResearchStatus.Completed || state.Status == ResearchStatus.Failed)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent loop failed");
            state!.Status = ResearchStatus.Failed;
            await _repository.UpdateAsync(state);
        }
    }

    public async Task RunIterationAsync(Guid researchId)
    {
        var state = await _repository.GetAsync(researchId);
        if (state == null) return;

        state.IterationCount++;
        _logger.LogInformation("--- Iteration {Iteration} for {TaskId} ---", state.IterationCount, researchId);

        // 1. Подготовка контекста (Prompt Engineering)
        var chatHistory = BuildChatHistory(state);

        // 2. Вызов LLM с требованием JSON Schema (Reasoning Phase)
        // Настройка для Structured Outputs (Strict Mode)
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ResponseFormat = typeof(ReasoningSchema), // Magic of Semantic Kernel & OpenAI SDK
            Temperature = 0.2,
            MaxTokens = 4000
        };

        try
        {
            var result = await _chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings: executionSettings
            );

            var responseContent = result.Content ?? "{}";
            
            // Сохраняем "мысли" агента в историю
            state.History.Add(new ChatMessage { Role = "assistant", Content = responseContent });
            await _repository.UpdateAsync(state);

            // 3. Десериализация и логика (Reasoning)
            var reasoning = JsonSerializer.Deserialize<ReasoningSchema>(responseContent);
            
            if (reasoning == null)
            {
                _logger.LogWarning("Failed to parse reasoning schema");
                return;
            }

            LogReasoning(reasoning);

            // 4. Выбор действия (Action Selection)
            if (reasoning.TaskCompleted)
            {
                await ExecuteFinalAnswerAsync(state, reasoning);
            }
            else
            {
                await ExecuteToolAsync(state, reasoning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during iteration");
            state.History.Add(new ChatMessage { Role = "system", Content = $"Error: {ex.Message}" });
            await _repository.UpdateAsync(state);
        }
    }

    private async Task ExecuteToolAsync(ResearchState state, ReasoningSchema reasoning)
    {
        string toolResult = "";
        string toolName = reasoning.NextToolName;

        try
        {
            switch (toolName.ToLower())
            {
                case "websearch":
                    // В реальном SGR аргументы - это JSON. Тут упрощаем, считаем что ToolArguments это строка запроса
                    toolResult = await _searchService.SearchAsync(reasoning.ToolArguments);
                    break;
                
                case "extractcontent":
                    // Пример парсинга аргументов, если они переданы как JSON список URL
                    // var urls = JsonSerializer.Deserialize<List<string>>(reasoning.ToolArguments);
                    // toolResult = await _searchService.ExtractContentAsync(urls);
                    toolResult = "Extract content simulated (requires real urls handling logic)";
                    break;

                default:
                    toolResult = $"Tool '{toolName}' not found.";
                    break;
            }
        }
        catch (Exception ex)
        {
            toolResult = $"Tool execution error: {ex.Message}";
        }

        // 5. Обновление контекста (Observation)
        _logger.LogInformation("Tool {ToolName} result length: {Length}", toolName, toolResult.Length);
        
        state.History.Add(new ChatMessage 
        { 
            Role = "tool", 
            Content = toolResult,
            ToolCallId = toolName // Условно используем имя как ID
        });
        
        await _repository.UpdateAsync(state);
    }

    private async Task ExecuteFinalAnswerAsync(ResearchState state, ReasoningSchema reasoning)
    {
        _logger.LogInformation("Task Completed. Report: {Report}", reasoning.ToolArguments);
        
        state.Status = ResearchStatus.Completed;
        state.FinalReport = reasoning.ToolArguments; // Предполагаем, что отчет лежит в аргументах
        await _repository.UpdateAsync(state);
    }

    private ChatHistory BuildChatHistory(ResearchState state)
    {
        var history = new ChatHistory();
        
        // System Prompt - Сердце SGR
        string systemPrompt = @"You are a generic research agent executing a Schema-Guided Reasoning (SGR) process.
Your goal is to provide accurate and concise information based on the user's task.

You operate in a loop: Reasoning -> Action -> Observation.

AVAILABLE TOOLS:
1. WebSearch: Search the internet. Args: query string.
2. ExtractContent: Get full page content. Args: list of URLs (json).
3. FinalAnswer: Return the final report. Args: the report text.

CRITICAL INSTRUCTION:
You MUST respond with a valid JSON object matching the 'ReasoningSchema'.
You must evaluate your current situation, decide if you have enough data, and select the next tool.
Do not output plain text, only JSON.";

        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage($"Current Date: {DateTime.UtcNow}\nResearch Task: {state.Task}");

        foreach (var msg in state.History)
        {
            if (msg.Role == "user") history.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant") history.AddAssistantMessage(msg.Content);
            else if (msg.Role == "tool") history.AddMessage(AuthorRole.Tool, msg.Content); // SK supports Tool role
            else history.AddSystemMessage(msg.Content);
        }

        return history;
    }

    private void LogReasoning(ReasoningSchema r)
    {
        _logger.LogInformation("\n[Reasoning]\nSituation: {Sit}\nNext Tool: {Tool}\nSteps: {Steps}\nEnough Data: {Enough}\n", 
            r.CurrentSituation, r.NextToolName, string.Join("->", r.RemainingSteps), r.EnoughData);
    }
}