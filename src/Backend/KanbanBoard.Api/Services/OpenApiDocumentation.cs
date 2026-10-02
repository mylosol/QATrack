using System.Reflection;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace KanbanBoard.Api.Services;

/// <summary>
/// OpenAPI 3.0 setup (spec 1 / 4.2): raw schema at <c>/api/openapi.json</c>
/// and Swagger UI at <c>/api/docs</c>. The document only describes the
/// secured <c>/api/v1</c> surface and uses stable operationIds so agents can
/// map operations directly to tool/function definitions.
/// </summary>
public static class OpenApiDocumentation
{
    public const string DocumentName = "v1";
    public const string SchemaPath = "/api/openapi.json";

    /// <summary>Separate document for the in-app issue reporting API (1.12.0).</summary>
    public const string ReportDocumentName = "report";
    public const string ReportSchemaPath = "/api/openapi-report.json";
    public const string ReporterKeySchemeId = "ReporterKey";
    public const string UiRoutePrefix = "api/docs";
    public const string ApiKeySchemeId = "ApiKey";
    public const string AgentIdentitySchemeId = "AgentIdentity";

    /// <summary>
    /// How agents make sure every human comment gets answered (1.9.0). Part of
    /// the document description and of GET /api/v1/meta.
    /// </summary>
    public const string AgentDiscussionRule =
        "Humans comment on cards to talk to you, often without moving them. At the start of every session, and " +
        "before picking up new work, call GET /api/v1/workitems?discussion=AwaitingAgent (GET /api/v1/meta also " +
        "reports awaitingAgentCount). For each card, GET it, read the history entries with isAiAction false and a " +
        "comment, act on them, and reply with POST /api/v1/workitems/{id}/comments (or a PATCH with a 'comment'). " +
        "Your comment clears AwaitingAgent; the card stays on that list until you reply.";

    /// <summary>
    /// How agents keep their knowledge of the API current (1.5.0). Part of the
    /// document description and of GET /api/v1/meta.
    /// </summary>
    public const string AgentContractRule =
        "Every /api/v1 response carries an `X-API-Schema-Version` header: a fingerprint of this OpenAPI document " +
        "that changes only when the API changes. Remember it (it is also `schemaVersion` in GET /api/v1/meta). " +
        "If a later response carries a different value, re-read /api/openapi.json before your next call, and call " +
        "GET /api/v1/meta?since=<version you knew> to read what changed. Changes within /api/v1 are additive; a " +
        "breaking change would ship as a new /api/v2. An HTTP 503 with an HTML body means the server is being " +
        "updated: wait a few seconds and retry.";

    /// <summary>Registers the Swashbuckle generator.</summary>
    public static IServiceCollection AddKanbanOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "QATrack Kanban API",
                // Application SemVer (the URL segment /api/v1 is the API contract version).
                Version = AppVersion.Current.Version,
                Description =
                    "REST API for AI agents to query, create, update, document and close QA work items. " +
                    "Every request must send the pre-shared `X-API-Key` header and an `X-Agent-Identity` " +
                    "header naming the agent (e.g. `Claude-Code-Agent-v1`). All mutations are recorded in the " +
                    "work item's audit history as AI actions and flag the card as AI-modified.\n\n" +
                    "**Staying current:** " + AgentContractRule + "\n\n" +
                    "**Answering humans:** " + AgentDiscussionRule,
            });

            options.SwaggerDoc(ReportDocumentName, new OpenApiInfo
            {
                Title = "QATrack in-app issue reporting API",
                Version = AppVersion.Current.Version,
                Description =
                    "Lets the programs under test file issues straight onto the QATrack board from a \"Report an issue\" " +
                    "feature. Send the reporter key in `X-Reporter-Key`. Reports are recorded as human reports (never as AI), " +
                    "land in New, are tagged `in-app-report`, and are authored by the `reporter` name you send.\n\n" +
                    "The reporter key ships inside the programs, so it is deliberately limited: it can only file reports and " +
                    "upload screenshots, cannot read or change the board, and requests are rate-limited per IP address " +
                    "(HTTP 429 when exceeded; wait and retry). To attach a screenshot, upload it with POST /api/report/attachments " +
                    "and put the returned `markdown` into the description. HTTP 503 with an HTML body means the server is " +
                    "being updated: retry shortly.",
            });

            // Each document lists only its own surface: the AI agent API (/api/v1)
            // for tool calling, and the in-app reporting API (/api/report).
            options.DocInclusionPredicate((docName, api) =>
            {
                var path = api.RelativePath ?? string.Empty;
                return docName == ReportDocumentName
                    ? path.StartsWith("api/report/", StringComparison.OrdinalIgnoreCase)
                    : path.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase);
            });
            options.DocumentFilter<ReportDocumentSecurityFilter>();

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml);
            }

            options.SupportNonNullableReferenceTypes();
            options.OperationFilter<ContractHeadersOperationFilter>();

            // Both headers are mandatory on every /api/v1 call (spec 4.1).
            options.AddSecurityDefinition(ApiKeySchemeId, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-API-Key",
                Description = "Pre-shared API key configured on the server (AiAgentApi:ApiKey).",
            });
            options.AddSecurityDefinition(AgentIdentitySchemeId, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Agent-Identity",
                Description = "Name/model of the calling agent, e.g. 'Claude-Code-Agent-v1'. Recorded on every change.",
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeySchemeId } }] = Array.Empty<string>(),
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = AgentIdentitySchemeId } }] = Array.Empty<string>(),
            });
        });

        return services;
    }

    /// <summary>
    /// Adds Swagger UI middleware. Must run BEFORE <c>UseRouting</c>: Swagger UI
    /// serves its JS/CSS through an internal StaticFileMiddleware, which skips
    /// any request routing has already matched to an endpoint - and the JSON
    /// 404 fallback for <c>/api/{**rest}</c> would otherwise claim them.
    /// </summary>
    public static IApplicationBuilder UseKanbanSwaggerUi(this IApplicationBuilder app)
    {
        return app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = UiRoutePrefix;
            // Relative so it also works when the site runs under an IIS virtual directory.
            options.SwaggerEndpoint("../openapi.json", "QATrack Kanban API v1");
            options.SwaggerEndpoint("../openapi-report.json", "QATrack in-app issue reporting");
            options.DocumentTitle = "QATrack API docs";
            options.DisplayOperationId();
        });
    }

    /// <summary>Maps the raw OpenAPI schema endpoint at <see cref="SchemaPath"/>.</summary>
    public static IEndpointRouteBuilder MapKanbanOpenApiSchema(this IEndpointRouteBuilder app)
    {
        // Swashbuckle's own middleware requires a {documentName} route token, so
        // the fixed spec path is served explicitly.
        app.MapGet(SchemaPath, (HttpRequest request, ISwaggerProvider provider, ApiContract contract) =>
            {
                var document = provider.GetSwagger(
                    DocumentName,
                    host: $"{request.Scheme}://{request.Host}",
                    basePath: request.PathBase.HasValue ? request.PathBase.Value : null);

                var json = ApiContract.Serialize(document);
                var etag = new EntityTagHeaderValue($"\"{ApiContract.Hash(json)[..16]}\"");

                var response = request.HttpContext.Response;
                response.Headers.ETag = etag.ToString();
                // Clients may cache it but must revalidate (a cheap 304 when unchanged).
                response.Headers.CacheControl = "no-cache";
                response.Headers[ApiContract.SchemaVersionHeader] = contract.SchemaVersion;

                var ifNoneMatch = request.GetTypedHeaders().IfNoneMatch;
                if (ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false)))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                return Results.Text(json, "application/json");
            })
            .ExcludeFromDescription();

        app.MapGet(ReportSchemaPath, (HttpRequest request, ISwaggerProvider provider) =>
            {
                var document = provider.GetSwagger(
                    ReportDocumentName,
                    host: $"{request.Scheme}://{request.Host}",
                    basePath: request.PathBase.HasValue ? request.PathBase.Value : null);
                request.HttpContext.Response.Headers.CacheControl = "no-cache";
                return Results.Text(ApiContract.Serialize(document), "application/json");
            })
            .ExcludeFromDescription();

        return app;
    }
}

/// <summary>
/// Documents the contract headers (X-API-Schema-Version, Link) on every
/// response in the OpenAPI document, so tool schemas know they exist.
/// </summary>
internal sealed class ContractHeadersOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Only the AI agent API carries the contract headers.
        if (context.ApiDescription.RelativePath?.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        foreach (var response in operation.Responses.Values)
        {
            response.Headers[ApiContract.SchemaVersionHeader] = new OpenApiHeader
            {
                Description = "Fingerprint of this OpenAPI document. When it changes, re-read /api/openapi.json.",
                Schema = new OpenApiSchema { Type = "string" },
            };
            response.Headers["Link"] = new OpenApiHeader
            {
                Description = "</api/openapi.json>; rel=\"service-desc\" - where to re-read this API's description.",
                Schema = new OpenApiSchema { Type = "string" },
            };
        }
    }
}

/// <summary>
/// The reporting document authenticates with <c>X-Reporter-Key</c> only, not
/// the AI agent headers (which are global security definitions).
/// </summary>
internal sealed class ReportDocumentSecurityFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (context.DocumentName != OpenApiDocumentation.ReportDocumentName)
        {
            return;
        }

        swaggerDoc.Components.SecuritySchemes.Clear();
        swaggerDoc.Components.SecuritySchemes[OpenApiDocumentation.ReporterKeySchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = KanbanBoard.Api.Middleware.IssueReportingOptions.KeyHeader,
            Description = "Reporter key (IssueReporting:ApiKey on the server). Only files reports and uploads screenshots.",
        };
        swaggerDoc.SecurityRequirements = new List<OpenApiSecurityRequirement>
        {
            new()
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = OpenApiDocumentation.ReporterKeySchemeId } }] = Array.Empty<string>(),
            },
        };
    }
}
