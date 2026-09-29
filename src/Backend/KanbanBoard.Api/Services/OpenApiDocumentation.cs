using System.Reflection;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

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
    public const string UiRoutePrefix = "api/docs";
    public const string ApiKeySchemeId = "ApiKey";
    public const string AgentIdentitySchemeId = "AgentIdentity";

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
                    "work item's audit history as AI actions and flag the card as AI-modified.",
            });

            // Only the AI-facing /api/v1 endpoints belong in the tool schema.
            options.DocInclusionPredicate((_, api) =>
                api.RelativePath?.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase) == true);

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
            {
                options.IncludeXmlComments(xml);
            }

            options.SupportNonNullableReferenceTypes();

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
            options.DocumentTitle = "QATrack API docs";
            options.DisplayOperationId();
        });
    }

    /// <summary>Maps the raw OpenAPI schema endpoint at <see cref="SchemaPath"/>.</summary>
    public static IEndpointRouteBuilder MapKanbanOpenApiSchema(this IEndpointRouteBuilder app)
    {
        // Swashbuckle's own middleware requires a {documentName} route token, so
        // the fixed spec path is served explicitly.
        app.MapGet(SchemaPath, (HttpRequest request, ISwaggerProvider provider) =>
            {
                var document = provider.GetSwagger(
                    DocumentName,
                    host: $"{request.Scheme}://{request.Host}",
                    basePath: request.PathBase.HasValue ? request.PathBase.Value : null);

                using var writer = new StringWriter();
                document.SerializeAsV3(new OpenApiJsonWriter(writer));
                return Results.Text(writer.ToString(), "application/json");
            })
            .ExcludeFromDescription();

        return app;
    }
}
