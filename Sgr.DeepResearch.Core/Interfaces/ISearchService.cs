namespace Sgr.DeepResearch.Core.Interfaces;

public interface ISearchService
{
    Task<string> SearchAsync(string query, int maxResults = 5);
    Task<string> ExtractContentAsync(List<string> urls);
}