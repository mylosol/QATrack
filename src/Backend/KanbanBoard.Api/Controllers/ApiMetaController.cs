using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanbanBoard.Api.Controllers;

/// <summary>API self-description for AI agents (1.5.0).</summary>
[ApiController]
[Route("api/v1/meta")]
[Tags("Meta")]
public sealed class ApiMetaController : ControllerBase
{
    private readonly ApiContract _contract;
    private readonly WorkItemService _items;

    public ApiMetaController(ApiContract contract, WorkItemService items)
    {
        _contract = contract;
        _items = items;
    }

    /// <summary>Check whether your knowledge of this API is current, and see what changed.</summary>
    /// <remarks>
    /// Call this at the start of a session. If <c>schemaVersion</c> differs from the one you
    /// remembered, re-read <c>openApiUrl</c> before making other calls. Pass <c>since</c> (the app
    /// version you last saw) to list only the API changes made after it.
    /// </remarks>
    /// <param name="since">Only list changes newer than this Semantic Version, e.g. 1.4.0.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet(Name = "getApiMeta")]
    [ProducesResponseType(typeof(ApiMetaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiMetaDto>> Get([FromQuery] string? since = null, CancellationToken ct = default)
    {
        if (since is not null && !AppVersion.IsValidSemVer(since))
        {
            throw new WorkItemValidationException(nameof(since), "since must be a Semantic Version such as 1.4.0.");
        }

        var pathBase = Request.PathBase;
        return Ok(new ApiMetaDto
        {
            Version = AppVersion.Current.Version,
            SchemaVersion = _contract.SchemaVersion,
            OpenApiUrl = $"{pathBase}{OpenApiDocumentation.SchemaPath}",
            DocsUrl = $"{pathBase}/{OpenApiDocumentation.UiRoutePrefix}",
            Instructions = OpenApiDocumentation.AgentContractRule + " " + OpenApiDocumentation.AgentDiscussionRule,
            AwaitingAgentCount = await _items.CountOnBoardAsync(new WorkItemQuery { Discussion = DiscussionStatus.AwaitingAgent }, ct),
            Changes = ApiChangeLog.Since(since),
        });
    }
}
