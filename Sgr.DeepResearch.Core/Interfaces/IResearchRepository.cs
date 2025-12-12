using Sgr.DeepResearch.Core.Models;

namespace Sgr.DeepResearch.Core.Interfaces;

public interface IResearchRepository
{
    Task<ResearchState> CreateAsync(string task);
    Task<ResearchState?> GetAsync(Guid id);
    Task UpdateAsync(ResearchState state);
}