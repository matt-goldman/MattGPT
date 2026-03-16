using MattGPT.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var (appConfig, configSeeder) = builder.AddAppConfiguration();
var infra = builder.AddInfrastructure();
var apiService = builder.AddApiService(appConfig, configSeeder, infra);
builder.AddUI(appConfig, configSeeder, apiService, infra);

builder.Build().Run();
