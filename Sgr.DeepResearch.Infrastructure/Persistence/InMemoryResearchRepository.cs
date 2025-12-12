using System.Collections.Concurrent;
using Sgr.DeepResearch.Core.Interfaces;
using Sgr.DeepResearch.Core.Models;

namespace Sgr.DeepResearch.Infrastructure.Persistence;

public class InMemoryResearchRepository : IResearchRepository
{
    private readonly ConcurrentDictionary<Guid, ResearchState> _store = new();

    public Task<ResearchState> CreateAsync(string task)
    {
        var state = new ResearchState
        {
            Id = Guid.NewGuid(),
            Task = task,
            Status = ResearchStatus.Created
        };
        _store.TryAdd(state.Id, state);
        return Task.FromResult(state);
    }

    public Task<ResearchState?> GetAsync(Guid id)
    {
        _store.TryGetValue(id, out var state);
        return Task.FromResult(state);
    }

    public Task UpdateAsync(ResearchState state)
    {
        state.LastUpdatedAt = DateTime.UtcNow;
        _store.AddOrUpdate(state.Id, state, (key, old) => state);
        return Task.CompletedTask;
    }
}