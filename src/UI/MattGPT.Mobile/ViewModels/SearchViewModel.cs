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

    public ObservableCollection<SearchResult> Results { get; } = [];

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
            var results = await searchService.SearchAsync(q, limit: 20);
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
