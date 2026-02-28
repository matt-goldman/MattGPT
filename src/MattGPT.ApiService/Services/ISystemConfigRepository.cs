using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

/// <summary>Stores and retrieves the system configuration document.</summary>
public interface ISystemConfigRepository
{
    /// <summary>Returns the current system config, or null if none has been stored.</summary>
    Task<SystemConfig?> GetAsync(CancellationToken ct = default);

    /// <summary>Upserts the system config document.</summary>
    Task UpsertAsync(SystemConfig config, CancellationToken ct = default);
}
