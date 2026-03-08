using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using MongoDB.Driver;

namespace MattGPT.ApiService.Services;

/// <summary>
/// MongoDB-backed implementation of <see cref="IProjectNameRepository"/>.
/// Stores user-assigned project names in a <c>project_names</c> collection.
/// </summary>
public class ProjectNameRepository : IProjectNameRepository
{
    private readonly IMongoCollection<ProjectName> _collection;

    public ProjectNameRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase("mattgptdb");
        _collection = db.GetCollection<ProjectName>("project_names");
    }

    /// <inheritdoc/>
    public async Task SetNameAsync(string templateId, string name, CancellationToken ct = default)
    {
        var filter = Builders<ProjectName>.Filter.Eq(x => x.TemplateId, templateId);
        var update = Builders<ProjectName>.Update
            .Set(x => x.Name, name)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> GetAllNamesAsync(CancellationToken ct = default)
    {
        var all = await _collection.Find(Builders<ProjectName>.Filter.Empty).ToListAsync(ct);
        return all.ToDictionary(p => p.TemplateId, p => p.Name);
    }
}
