var builder = DistributedApplication.CreateBuilder(args);

// Postgres container, data persisted across restarts. The database resource is named
// "postgres" so its connection string lands as ConnectionStrings:postgres — which the
// API reads (case-insensitively) as GetConnectionString("Postgres").
var postgresServer = builder.AddPostgres("pg")
    .WithDataVolume("first10-pg-data");
var database = postgresServer.AddDatabase("postgres", databaseName: "first10");

var api = builder.AddProject<Projects.First10_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

// Vite dev server. vite.config.ts reads PORT and services__api__http__0 (injected by
// WithReference) to bind its port and proxy /api + /hubs to the API.
builder.AddNpmApp("web", "../../web", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 5173, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
