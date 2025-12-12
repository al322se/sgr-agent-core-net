using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Sgr.DeepResearch.Core.Models;

/// <summary>
/// Основная схема SGR (Schema-Guided Reasoning).
/// Модель обязана вернуть ответ, строго соответствующий этому классу.
/// </summary>
public class ReasoningSchema
{
    [JsonPropertyName("reasoning_steps")]
    [Description("Step-by-step reasoning (brief, 1 sentence each)")]
    public List<string> ReasoningSteps { get; set; } = new();

    [JsonPropertyName("current_situation")]
    [Description("Current research situation (2-3 sentences MAX)")]
    public string CurrentSituation { get; set; } = string.Empty;

    [JsonPropertyName("enough_data")]
    [Description("Sufficient data collected for comprehensive report?")]
    public bool EnoughData { get; set; }

    [JsonPropertyName("remaining_steps")]
    [Description("1-3 remaining steps (brief, action-oriented)")]
    public List<string> RemainingSteps { get; set; } = new();

    [JsonPropertyName("task_completed")]
    [Description("Is the research task finished?")]
    public bool TaskCompleted { get; set; }

    /// <summary>
    /// Имя инструмента, который агент хочет вызвать следующим.
    /// Если TaskCompleted = true, здесь должно быть 'FinalAnswer'.
    /// </summary>
    [JsonPropertyName("next_tool_name")]
    [Description("The name of the tool to execute next. Options: 'WebSearch', 'ExtractContent', 'FinalAnswer'.")]
    public string NextToolName { get; set; } = string.Empty;

    /// <summary>
    /// Аргументы для инструмента (например, поисковый запрос).
    /// </summary>
    [JsonPropertyName("tool_arguments")]
    [Description("The arguments for the selected tool.")]
    public string ToolArguments { get; set; } = string.Empty;
}