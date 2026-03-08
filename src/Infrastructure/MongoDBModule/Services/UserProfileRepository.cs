using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MattGPT.MongoDBModule.Services;

/// <summary>
/// MongoDB-backed implementation of <see cref="IUserProfileRepository"/>.
/// Maintains a single document keyed by the fixed ID "user-profile".
/// </summary>
public class UserProfileRepository(IMongoClient mongoClient, ILogger<UserProfileRepository> logger)
    : IUserProfileRepository
{
    private const string DatabaseName = "mattgptdb";
    private const string CollectionName = "user_profiles";

    private IMongoCollection<UserProfile> Collection =>
        mongoClient.GetDatabase(DatabaseName).GetCollection<UserProfile>(CollectionName);

    public async Task<UserProfile?> GetAsync(CancellationToken ct = default)
    {
        var cursor = await Collection.FindAsync(
            Builders<UserProfile>.Filter.Eq(p => p.Id, "user-profile"), cancellationToken: ct);
        return await cursor.FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(UserProfile profile, CancellationToken ct = default)
    {
        await Collection.ReplaceOneAsync(
            Builders<UserProfile>.Filter.Eq(p => p.Id, profile.Id),
            profile,
            new ReplaceOptions { IsUpsert = true },
            ct);

        logger.LogDebug("Upserted user profile (source create_time: {CreateTime}).", profile.SourceCreateTime);
    }
}
