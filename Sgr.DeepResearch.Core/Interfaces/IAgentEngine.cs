using Sgr.DeepResearch.Core.Models;

namespace Sgr.DeepResearch.Core.Interfaces;

public interface IAgentEngine
{
    /// <summary>
    /// Запускает одну итерацию цикла Reasoning -> Action.
    /// </summary>
    Task RunIterationAsync(Guid researchId);
    
    /// <summary>
    /// Запускает полный цикл до завершения.
    /// </summary>
    Task RunFullLoopAsync(Guid researchId, CancellationToken ct = default);
}