using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using MongoDB.Driver;

namespace MattGPT.MongoDBModule.Services;

/// <summary>MongoDB-backed implementation of <see cref="INotificationRepository"/>.</summary>
public class NotificationRepository : INotificationRepository
{
    private readonly IMongoCollection<Notification> _collection;

    public NotificationRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase("mattgptdb");
        _collection = db.GetCollection<Notification>("notifications");

        var keys = Builders<Notification>.IndexKeys;
        _collection.Indexes.CreateMany([
            new CreateIndexModel<Notification>(keys.Ascending(x => x.UserId).Descending(x => x.CreatedAt)),
            new CreateIndexModel<Notification>(keys.Ascending(x => x.UserId).Ascending(x => x.IsRead)),
        ]);
    }

    /// <inheritdoc/>
    public Task CreateAsync(Notification notification, CancellationToken ct = default)
        => _collection.InsertOneAsync(notification, cancellationToken: ct);

    /// <inheritdoc/>
    public Task<List<Notification>> ListRecentAsync(string? userId, int limit, CancellationToken ct = default)
        => _collection
            .Find(Builders<Notification>.Filter.Eq(x => x.UserId, userId))
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public Task<long> CountUnreadAsync(string? userId, CancellationToken ct = default)
        => _collection.CountDocumentsAsync(
            Builders<Notification>.Filter.Eq(x => x.UserId, userId) & Builders<Notification>.Filter.Eq(x => x.IsRead, false),
            cancellationToken: ct);

    /// <inheritdoc/>
    public async Task MarkReadAsync(string? userId, IReadOnlyCollection<string>? ids, CancellationToken ct = default)
    {
        var filter = Builders<Notification>.Filter.Eq(x => x.UserId, userId)
            & Builders<Notification>.Filter.Eq(x => x.IsRead, false);
        if (ids is not null)
            filter &= Builders<Notification>.Filter.In(x => x.Id, ids);

        await _collection.UpdateManyAsync(filter, Builders<Notification>.Update.Set(x => x.IsRead, true), cancellationToken: ct);
    }
}
