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
    private const int MaxIterations = 10;

    public SgrAgentEngine(
        IResearchRepository repository,
        ISearchService searchService,
        Kernel kernel, 
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
                
                // Перечитываем состояние
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

        // 1. Подготовка контекста
        var chatHistory = BuildChatHistory(state);
        
        // ЛОГИРОВАНИЕ: Выводим структуру чата перед отправкой, чтобы видеть, что уходит в LLM
        LogChatHistoryDebug(chatHistory);

        // 2. Настройка вызова
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ResponseFormat = typeof(ReasoningSchema), 
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
            
            // Сохраняем "мысли" агента
            state.History.Add(new ChatMessage { Role = "assistant", Content = responseContent });
            await _repository.UpdateAsync(state);

            // 3. Десериализация
            ReasoningSchema? reasoning;
            try 
            {
                reasoning = JsonSerializer.Deserialize<ReasoningSchema>(responseContent);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON Parse Error. Content: {Content}", responseContent);
                // Если модель вернула битый JSON, добавляем системное сообщение и пробуем дальше (или фейлим)
                state.History.Add(new ChatMessage { Role = "system", Content = "Error parsing JSON response. Please ensure valid JSON format." });
                await _repository.UpdateAsync(state);
                return;
            }
            
            if (reasoning == null)
            {
                _logger.LogWarning("Reasoning schema is null");
                return;
            }

            LogReasoning(reasoning);

            // 4. Выбор действия
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
            _logger.LogError(ex, "Error during iteration execution");
            state.History.Add(new ChatMessage { Role = "system", Content = $"Error executing iteration: {ex.Message}" });
            await _repository.UpdateAsync(state);
            throw; // Пробрасываем, чтобы цикл остановился или обработался выше
        }
    }

    private async Task ExecuteToolAsync(ResearchState state, ReasoningSchema reasoning)
    {
        string toolResult = "";
        string toolName = reasoning.NextToolName;

        _logger.LogInformation("Executing Tool: {ToolName} with Args: {Args}", toolName, reasoning.ToolArguments);

        try
        {
            switch (toolName.ToLower())
            {
                case "websearch":
                    toolResult = await _searchService.SearchAsync(reasoning.ToolArguments);
                    break;
                
                case "extractcontent":
                    // Здесь ожидаем, что аргументы - это JSON массив URL или один URL
                    // Простая эмуляция: если это просто строка с URL, оборачиваем в список
                    var urls = new List<string> { reasoning.ToolArguments };
                    toolResult = await _searchService.ExtractContentAsync(urls);
                    break;

                default:
                    toolResult = $"Tool '{toolName}' not found or not supported.";
                    break;
            }
        }
        catch (Exception ex)
        {
            toolResult = $"Tool execution error: {ex.Message}";
            _logger.LogError(ex, "Tool failed");
        }

        _logger.LogInformation("Tool Result Length: {Length}", toolResult.Length);
        
        // 5. Сохранение результата
        // Важно: Сохраняем как 'tool' в БД для семантики, но в BuildChatHistory превратим в UserMessage
        state.History.Add(new ChatMessage 
        { 
            Role = "tool", 
            Content = toolResult,
            ToolCallId = toolName // Используем имя инструмента как ID для наглядности
        });
        
        await _repository.UpdateAsync(state);
    }

    private async Task ExecuteFinalAnswerAsync(ResearchState state, ReasoningSchema reasoning)
    {
        _logger.LogInformation("Task Completed. Report generated.");
        
        state.Status = ResearchStatus.Completed;
        state.FinalReport = reasoning.ToolArguments;
        await _repository.UpdateAsync(state);
    }

    private ChatHistory BuildChatHistory(ResearchState state)
    {
        var history = new ChatHistory();
        
        string systemPrompt = @"You are a generic research agent executing a Schema-Guided Reasoning (SGR) process.
Your goal is to provide accurate and concise information based on the user's task.

You operate in a loop: Reasoning -> Action -> Observation.

AVAILABLE TOOLS:
1. WebSearch: Search the internet. Args: query string.
2. ExtractContent: Get full page content. Args: url string.
3. FinalAnswer: Return the final report. Args: the report text.

CRITICAL INSTRUCTION:
You MUST respond with a valid JSON object matching the 'ReasoningSchema'.
You must evaluate your current situation, decide if you have enough data, and select the next tool.
Do not output plain text, only JSON.";

        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage($"Current Date: {DateTime.UtcNow}\nResearch Task: {state.Task}");

        foreach (var msg in state.History)
        {
            if (msg.Role == "user") 
            {
                history.AddUserMessage(msg.Content);
            }
            else if (msg.Role == "assistant") 
            {
                history.AddAssistantMessage(msg.Content);
            }
            else if (msg.Role == "tool") 
            {
                // ИСПРАВЛЕНИЕ: Semantic Kernel падает, если использовать AuthorRole.Tool без валидного Call ID и предыдущего вызова.
                // В архитектуре SGR мы используем "виртуальные" вызовы через JSON.
                // Поэтому результаты инструментов подаем как Observation от пользователя.
                string observationPrefix = !string.IsNullOrEmpty(msg.ToolCallId) 
                    ? $"[Observation from {msg.ToolCallId}]:" 
                    : "[Observation]:";
                
                history.AddUserMessage($"{observationPrefix}\n{msg.Content}");
            }
            else 
            {
                history.AddSystemMessage(msg.Content);
            }
        }

        return history;
    }

    private void LogChatHistoryDebug(ChatHistory history)
    {
        _logger.LogInformation(">>> CHAT HISTORY PREVIEW ({Count} msgs) <<<", history.Count);
        foreach (var msg in history)
        {
            // Логируем только начало сообщений, чтобы не засорять консоль
            string preview = msg.Content?.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content ?? "";
            _logger.LogInformation("[{Role}]: {Content}", msg.Role, preview);
        }
        _logger.LogInformation(">>> END PREVIEW <<<");
    }

    private void LogReasoning(ReasoningSchema r)
    {
        _logger.LogInformation("\n[Reasoning]\nSituation: {Sit}\nNext Tool: {Tool}\nStep: {Steps}\nEnough Data: {Enough}\n", 
            r.CurrentSituation, r.NextToolName, string.Join("->", r.ReasoningSteps.Take(1)), r.EnoughData);
    }
}