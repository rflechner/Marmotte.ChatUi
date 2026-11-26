using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var webPort = builder.Configuration.GetValue("Infrastructure:WebPort", 5180);
var keycloakPort = builder.Configuration.GetValue("Infrastructure:KeycloakPort", 8180);
var pgAdminPort = builder.Configuration.GetValue("Infrastructure:PgAdminPort", 5050);
int[] servicePorts = [webPort, keycloakPort, pgAdminPort];
if (servicePorts.Any(port => port is < 1024 or > 65535) || servicePorts.Distinct().Count() != servicePorts.Length)
{
    throw new InvalidOperationException("Infrastructure ports must be distinct and between 1024 and 65535.");
}

var webOrigin = $"http://localhost:{webPort.ToString(CultureInfo.InvariantCulture)}";
var keycloakOrigin = $"http://localhost:{keycloakPort.ToString(CultureInfo.InvariantCulture)}";
const string postgresImage = "pgvector/pgvector";
const string postgresTag = "0.8.7-pg17-bookworm";
const string postgresDigest = "b513c48566e4777cd3370451400000c78eda6a08131d68f4c31d4a76f27bd8fd";

var postgres = builder.AddPostgres("postgres")
    .WithImage(postgresImage, postgresTag)
    .WithImageSHA256(postgresDigest)
    // pgvector's tag does not begin with the PostgreSQL major version; specify the PG17 path.
    .WithVolume(builder.Configuration["Infrastructure:PostgresVolume"] ?? "marmotte-chatui-postgres-data", "/var/lib/postgresql/data");

postgres.WithPgAdmin(pgAdmin => pgAdmin
    .WithImageTag("9.15.0")
    .WithImageSHA256("cefc4cc6b7d9d71a9e81e76fb9d7e14038ab5661b539a190eea1b63fa612589a")
    .WithHostPort(pgAdminPort)
    .WaitFor(postgres));

var chatDatabase = postgres.AddDatabase("chatdb", "marmotte_chat");
var identityDatabase = postgres.AddDatabase("keycloakdb", "marmotte_identity");

// Run after database readiness on every start, including when the volume already exists.
// psql stops on SQL errors; Web cannot start when extension activation fails.
var databaseInitialization = builder.AddContainer("database-init", postgresImage, postgresTag)
    .WithImageSHA256(postgresDigest)
    .WithEnvironment("PGHOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("PGPORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("PGUSER", postgres.Resource.UserNameReference)
    .WithEnvironment("PGPASSWORD", postgres.Resource.PasswordParameter)
    .WithEnvironment("PGDATABASE", chatDatabase.Resource.DatabaseName)
    .WithEnvironment("PGCONNECT_TIMEOUT", "15")
    .WithEnvironment("PGOPTIONS", "-c statement_timeout=30000 -c lock_timeout=10000")
    .WithEntrypoint("psql")
    .WithArgs("--no-psqlrc", "--no-password", "--set=ON_ERROR_STOP=1", "--command=CREATE EXTENSION IF NOT EXISTS vector;")
    .WaitFor(chatDatabase);

var clientSecret = builder.AddParameter("keycloak-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

var keycloak = builder.AddKeycloak("keycloak", keycloakPort)
    .WithImageTag("26.6.4")
    .WithImageSHA256("0aae0de7fca85525f727d3354df17896092de8bb26ae4c12d89c77e5df8cbce4")
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL", ReferenceExpression.Create($"jdbc:postgresql://{postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}:{postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}/{identityDatabase.Resource.DatabaseName}"))
    .WithEnvironment("KC_DB_USERNAME", postgres.Resource.UserNameReference)
    .WithEnvironment("KC_DB_PASSWORD", postgres.Resource.PasswordParameter)
    .WithEnvironment("KC_HOSTNAME", keycloakOrigin)
    .WithEnvironment("MARMOTTE_WEB_ORIGIN", webOrigin)
    .WithEnvironment("MARMOTTE_CLIENT_SECRET", clientSecret)
    .WithRealmImport("Realms")
    .WaitFor(identityDatabase);

// Keep this loopback development authority stable even when Aspire has an HTTPS developer certificate.
// Aspire 13.6 exposes this per-resource opt-out through an experimental API.
#pragma warning disable ASPIRECERTIFICATES001
keycloak.WithoutHttpsCertificate();
#pragma warning restore ASPIRECERTIFICATES001

builder.AddProject<Projects.Marmotte_ChatUi_Web>("web")
    .WithEndpoint("http", endpoint => endpoint.Port = webPort)
    .WithReference(chatDatabase)
    .WithReference(keycloak)
    .WithEnvironment("Authentication__Authority", $"{keycloakOrigin}/realms/marmotte")
    .WithEnvironment("Authentication__ClientId", "marmotte-web")
    .WithEnvironment("Authentication__ClientSecret", clientSecret)
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(databaseInitialization)
    .WaitFor(keycloak);

builder.Build().Run();
