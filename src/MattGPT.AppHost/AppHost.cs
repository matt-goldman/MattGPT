var builder = DistributedApplication.CreateBuilder(args);

var mongodb = builder.AddMongoDB("mongodb")
    .AddDatabase("mattgptdb");

var qdrant = builder.AddQdrant("qdrant");

var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(mongodb)
    .WaitFor(mongodb)
    .WithReference(qdrant)
    .WaitFor(qdrant);

builder.AddProject<Projects.MattGPT_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
