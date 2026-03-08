using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using MattGPT.MongoDBModule.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace MattGPT.MongoDBModule;

public static class Module
{
    public static IHostApplicationBuilder AddMongoDBModule(this IHostApplicationBuilder builder)
    {
        // Map domain model id properties to MongoDB _id without annotating the Contracts models.
        BsonClassMap.RegisterClassMap<StoredConversation>(cm => { cm.AutoMap(); cm.MapIdProperty(c => c.ConversationId); });
        BsonClassMap.RegisterClassMap<ChatSession>(cm => { cm.AutoMap(); cm.MapIdProperty(c => c.SessionId).SetSerializer(new GuidSerializer(BsonType.String)); });
        BsonClassMap.RegisterClassMap<ProjectName>(cm => { cm.AutoMap(); cm.MapIdProperty(c => c.TemplateId); });
        BsonClassMap.RegisterClassMap<UserProfile>(cm => { cm.AutoMap(); cm.MapIdProperty(c => c.Id); });
        BsonClassMap.RegisterClassMap<SystemConfig>(cm => { cm.AutoMap(); cm.MapIdProperty(c => c.Id); });

        builder.AddMongoDBClient("mattgptdb");

        builder.Services.AddSingleton<IConversationRepository, ConversationRepository>();
        builder.Services.AddSingleton<IProjectNameRepository, ProjectNameRepository>();
        builder.Services.AddSingleton<IUserProfileRepository, UserProfileRepository>();
        builder.Services.AddSingleton<ISystemConfigRepository, SystemConfigRepository>();
        builder.Services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();

        return builder;
    }

    public static IHost UseMongoDBModule(this IHost host) => host;
}
