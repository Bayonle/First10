var builder = DistributedApplication.CreateBuilder(args);

// Postgres container, data persisted across restarts. The database resource is named
// "postgres" so its connection string lands as ConnectionStrings:postgres — which the
// API reads (case-insensitively) as GetConnectionString("Postgres").
var postgresServer = builder.AddPostgres("pg")
    .WithDataVolume("first10-pg-data");
var database = postgresServer.AddDatabase("postgres", databaseName: "first10");

// MinIO: S3-compatible object storage for media (D-016). Console UI on the "console"
// endpoint (login first10 / first10dev) for inspecting stored media.
var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", "first10")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "first10dev")
    .WithVolume("first10-minio-data", "/data")
    .WithHttpEndpoint(targetPort: 9000, name: "s3")
    .WithHttpEndpoint(targetPort: 9001, name: "console");

var api = builder.AddProject<Projects.First10_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WaitFor(minio)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Media__S3__ServiceUrl", minio.GetEndpoint("s3"))
    .WithEnvironment("Media__S3__AccessKey", "first10")
    .WithEnvironment("Media__S3__SecretKey", "first10dev");

// Vite dev server. vite.config.ts reads PORT and services__api__http__0 (injected by
// WithReference) to bind its port and proxy /api + /hubs to the API.
builder.AddNpmApp("web", "../../web", "dev")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 5173, env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
