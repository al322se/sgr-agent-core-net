using System.Text;
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

    private const int MaxIterations = 15;
    // Ограничиваем размер контента от одного инструмента, чтобы не забить контекст (например, 15к символов)
    private const int MaxToolOutputLength = 15000; 

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

        var chatHistory = BuildChatHistory(state);
        
        // LogChatHistoryDebug(chatHistory); // Можно раскомментировать для отладки

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ResponseFormat = typeof(ReasoningSchema), 
            Temperature = 0.2,
            MaxTokens = 4000
        };

        try
        {
            var result = await _chatService.GetChatMessageContentAsync(chatHistory, executionSettings: executionSettings);
            var responseContent = result.Content ?? "{}";
            
            state.History.Add(new ChatMessage { Role = "assistant", Content = responseContent });
            await _repository.UpdateAsync(state);

            ReasoningSchema? reasoning;
            try 
            {
                reasoning = JsonSerializer.Deserialize<ReasoningSchema>(responseContent);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON Parse Error");
                state.History.Add(new ChatMessage { Role = "system", Content = "Error parsing JSON. Please ensure valid JSON format." });
                await _repository.UpdateAsync(state);
                return;
            }
            
            if (reasoning == null) return;

            LogReasoning(reasoning);

            if (reasoning.TaskCompleted || reasoning.NextToolName.Equals("FinalAnswer", StringComparison.OrdinalIgnoreCase))
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
            state.History.Add(new ChatMessage { Role = "system", Content = $"System Error: {ex.Message}" });
            await _repository.UpdateAsync(state);
        }
    }

    private async Task ExecuteToolAsync(ResearchState state, ReasoningSchema reasoning)
    {
        string toolResult = "";
        string toolName = reasoning.NextToolName;
        string cleanArgs = reasoning.ToolArguments?.Trim().Trim('"').Trim('\'') ?? "";

        _logger.LogInformation("Tool: {Tool}, Args: {Args}", toolName, cleanArgs);

        try
        {
            switch (toolName.ToLower())
            {
                case "websearch":
                    toolResult = await _searchService.SearchAsync(cleanArgs);
                    break;
                
                case "extractcontent":
                    List<string> urls;
                    if (cleanArgs.StartsWith("[") && cleanArgs.EndsWith("]"))
                    {
                        try { urls = JsonSerializer.Deserialize<List<string>>(cleanArgs) ?? new(); }
                        catch { urls = new List<string> { cleanArgs }; }
                    }
                    else
                    {
                        urls = new List<string> { cleanArgs };
                    }
                    
                    toolResult = await _searchService.ExtractContentAsync(urls);
                    break;

                default:
                    toolResult = $"Tool '{toolName}' not found. Available tools: WebSearch, ExtractContent, FinalAnswer.";
                    break;
            }
        }
        catch (Exception ex)
        {
            toolResult = $"Tool execution error: {ex.Message}";
        }

        // Очистка и обрезка результата (Token Saving)
        if (toolResult.Length > MaxToolOutputLength)
        {
            _logger.LogWarning("Tool output truncated from {Len} to {Max}", toolResult.Length, MaxToolOutputLength);
            toolResult = toolResult.Substring(0, MaxToolOutputLength) + "\n...[TRUNCATED]...";
        }

        state.History.Add(new ChatMessage 
        { 
            Role = "tool", 
            Content = toolResult,
            ToolCallId = toolName 
        });
        
        await _repository.UpdateAsync(state);
    }

    private async Task ExecuteFinalAnswerAsync(ResearchState state, ReasoningSchema reasoning)
    {
        _logger.LogInformation("Task Completed.");
        
        state.Status = ResearchStatus.Completed;
        state.FinalReport = reasoning.ToolArguments;
        
        state.History.Add(new ChatMessage 
        { 
            Role = "tool", 
            Content = "Research completed.", 
            ToolCallId = "FinalAnswer" 
        });

        await _repository.UpdateAsync(state);
    }

    private ChatHistory BuildChatHistory(ResearchState state)
    {
        var history = new ChatHistory();
        
        // Улучшенный промпт
        string systemPrompt = @"You are a Deep Research Agent (SGR architecture).
Goal: Provide comprehensive, fact-checked answers based on gathered data.

LOOP:
1. Reasoning: Analyze current state.
2. Action: Select tool (WebSearch, ExtractContent).
3. Observation: Read tool output.
4. Repeat until data is sufficient.

TOOLS:
- WebSearch(query): Search Google/Tavily.
- ExtractContent(url): Read FULL text of a webpage. USE THIS for deep details.
- FinalAnswer(report): Return the final DETAILED report. 

CRITICAL RULES:
- RESPONSE MUST BE JSON 'ReasoningSchema'.
- When calling 'FinalAnswer', the 'tool_arguments' field MUST contain the FULL FINAL REPORT in Markdown format. Do NOT just write the title. Write the whole analysis.
- Clean URLs before extracting (no quotes).";

        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage($"Task: {state.Task}");

        foreach (var msg in state.History)
        {
            if (msg.Role == "user") history.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant") history.AddAssistantMessage(msg.Content);
            else if (msg.Role == "tool") 
            {
                string prefix = string.IsNullOrEmpty(msg.ToolCallId) ? "[Observation]" : $"[Observation from {msg.ToolCallId}]";
                history.AddUserMessage($"{prefix}\n{msg.Content}");
            }
            else history.AddSystemMessage(msg.Content);
        }

        return history;
    }

    private void LogChatHistoryDebug(ChatHistory history)
    {
        _logger.LogInformation(">>> CHAT HISTORY PREVIEW ({Count} msgs) <<<", history.Count);
        foreach (var msg in history)
        {
            string preview = msg.Content?.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content ?? "";
            _logger.LogInformation("[{Role}]: {Content}", msg.Role, preview);
        }
        _logger.LogInformation(">>> END PREVIEW <<<");
    }

    private void LogReasoning(ReasoningSchema r)
    {
        _logger.LogInformation("Reasoning: {Sit} | Tool: {Tool}", r.CurrentSituation, r.NextToolName);
    }
}