using System.Security.Cryptography;
using System.Text;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Tells AI agents when the <c>/api/v1</c> contract changed so they re-read the
/// OpenAPI document (1.5.0).
/// </summary>
/// <remarks>
/// The <see cref="SchemaVersion"/> is a fingerprint of the OpenAPI document
/// itself, NOT the app version: it changes exactly when an endpoint, field,
/// parameter or description changes, and stays the same across UI-only
/// releases. It excludes <c>info.version</c> and <c>servers</c> (which vary by
/// release and by host), so every server running the same API reports the
/// same value.
/// </remarks>
public sealed class ApiContract
{
    /// <summary>Response header carrying <see cref="SchemaVersion"/> on every /api/v1 response.</summary>
    public const string SchemaVersionHeader = "X-API-Schema-Version";

    /// <summary>RFC 8631 link relation for "machine-readable description of this API".</summary>
    public const string ServiceDescRelation = "service-desc";

    /// <summary>Length of the hex fingerprint (48 bits: collision-free for practical purposes).</summary>
    public const int FingerprintLength = 12;

    private readonly Lazy<string> _schemaVersion;

    public ApiContract(IServiceScopeFactory scopes, ILogger<ApiContract> logger)
    {
        _schemaVersion = new Lazy<string>(() =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var document = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>()
                    .GetSwagger(OpenApiDocumentation.DocumentName);
                return Fingerprint(document);
            }
            catch (Exception ex)
            {
                // Never fail API calls over the fingerprint; agents simply see "unknown".
                logger.LogError(ex, "Could not compute the API schema version.");
                return "unknown";
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Fingerprint of the current /api/v1 contract, e.g. <c>3f9c1a7e2b04</c>.</summary>
    public string SchemaVersion => _schemaVersion.Value;

    /// <summary>
    /// Hashes an OpenAPI document, ignoring <c>info.version</c> and <c>servers</c>.
    /// Mutates <paramref name="document"/>; pass a freshly generated one.
    /// </summary>
    public static string Fingerprint(OpenApiDocument document)
    {
        document.Info.Version = string.Empty;
        document.Servers?.Clear();
        return Hash(Serialize(document))[..FingerprintLength];
    }

    /// <summary>OpenAPI 3.0 JSON exactly as served at /api/openapi.json.</summary>
    public static string Serialize(OpenApiDocument document)
    {
        using var writer = new StringWriter();
        document.SerializeAsV3(new OpenApiJsonWriter(writer));
        return writer.ToString();
    }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 text.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>The <c>Link</c> header value pointing at the OpenAPI document.</summary>
    public static string LinkHeader(PathString pathBase) =>
        $"<{pathBase}{OpenApiDocumentation.SchemaPath}>; rel=\"{ServiceDescRelation}\"";
}
