using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Provides persistence for user-assigned project display names.
/// </summary>
public interface IProjectNameRepository
{
    /// <summary>Set or update the display name for a project.</summary>
    Task SetNameAsync(string templateId, string name, CancellationToken ct = default);

    /// <summary>Get all user-assigned project names, keyed by template ID.</summary>
    Task<Dictionary<string, string>> GetAllNamesAsync(CancellationToken ct = default);
}
