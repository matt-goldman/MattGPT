using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MattGPT.ApiClient.Models;
using MattGPT.ApiClient.Services;
using System.Collections.ObjectModel;

namespace MattGPT.Mobile.ViewModels;

public partial class SearchViewModel(ISearchService searchService) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// When set, search matches the literal words typed instead of the meaning of the query.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSemanticSearch))]
    [NotifyPropertyChangedFor(nameof(SearchModeHint))]
    private bool _isKeywordSearch;

    /// <summary>
    /// Inverse of <see cref="IsKeywordSearch"/>, for the relevance figure, which only means
    /// anything for semantic results — keyword scores are ranks relative to the top hit.
    /// </summary>
    public bool IsSemanticSearch => !IsKeywordSearch;

    public string SearchModeHint => IsKeywordSearch
        ? "Matching the exact words you type."
        : "Matching by meaning, not exact words.";

    public ObservableCollection<SearchResult> Results { get; } = [];

    /// <summary>
    /// Re-runs the current query when the mode is switched, so the toggle reads as
    /// "show me these results the other way" rather than as a setting to apply by hand.
    /// </summary>
    partial void OnIsKeywordSearchChanged(bool value)
    {
        if (SearchCommand.CanExecute(null))
            SearchCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var q = Query.Trim();
        if (string.IsNullOrEmpty(q)) return;

        IsSearching = true;
        ErrorMessage = string.Empty;
        Results.Clear();

        try
        {
            var mode = IsKeywordSearch ? SearchMode.Keyword : SearchMode.Semantic;
            var results = await searchService.SearchAsync(q, limit: 20, mode);
            foreach (var result in results)
                Results.Add(result);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(Query);
}
