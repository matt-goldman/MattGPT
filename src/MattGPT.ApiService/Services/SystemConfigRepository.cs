using MattGPT.ApiService.Models;
using MongoDB.Driver;

namespace MattGPT.ApiService.Services;

/// <summary>
/// MongoDB-backed implementation of <see cref="ISystemConfigRepository"/>.
/// Maintains a single document keyed by the fixed ID "system-config".
/// </summary>
public class SystemConfigRepository(IMongoClient mongoClient, ILogger<SystemConfigRepository> logger)
    : ISystemConfigRepository
{
    private const string DatabaseName = "mattgptdb";
    private const string CollectionName = "system_config";

    private IMongoCollection<SystemConfig> Collection =>
        mongoClient.GetDatabase(DatabaseName).GetCollection<SystemConfig>(CollectionName);

    public async Task<SystemConfig?> GetAsync(CancellationToken ct = default)
    {
        var cursor = await Collection.FindAsync(
            Builders<SystemConfig>.Filter.Eq(c => c.Id, "system-config"), cancellationToken: ct);
        return await cursor.FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(SystemConfig config, CancellationToken ct = default)
    {
        await Collection.ReplaceOneAsync(
            Builders<SystemConfig>.Filter.Eq(c => c.Id, config.Id),
            config,
            new ReplaceOptions { IsUpsert = true },
            ct);

        logger.LogDebug("Upserted system config.");
    }
}
