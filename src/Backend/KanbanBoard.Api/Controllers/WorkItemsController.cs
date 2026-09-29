using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanbanBoard.Api.Controllers;

/// <summary>
/// Secured work item API for autonomous AI agents (spec 4.2).
/// Every request must carry <c>X-API-Key</c> and <c>X-Agent-Identity</c>.
/// Mutations are automatically tagged as AI actions in the audit trail.
/// </summary>
[ApiController]
[Route("api/v1/workitems")]
[Tags("Work Items")]
public sealed class WorkItemsController : ControllerBase
{
    private readonly WorkItemService _service;

    public WorkItemsController(WorkItemService service)
    {
        _service = service;
    }

    /// <summary>List work items.</summary>
    /// <remarks>
    /// Returns work items ordered by id. All filters are optional and combine with AND.
    /// Use <c>assignedTo=unassigned</c> to find items with no assignee.
    /// </remarks>
    /// <param name="query">Optional filters: type, state, aiModified, assignedTo, top.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet(Name = "listWorkItems")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<WorkItemDto>>> List([FromQuery] WorkItemQuery query, CancellationToken ct)
        => Ok(await _service.ListAsync(query, ct));

    /// <summary>Get a work item with its full audit history.</summary>
    /// <param name="id">Work item id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:int}", Name = "getWorkItem")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkItemDto>> Get(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, ct));

    /// <summary>Create a bug, feature request or other work item.</summary>
    /// <param name="request">Title and type are required; everything else has defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost(Name = "createWorkItem")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkItemDto>> Create([FromBody] CreateWorkItemRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(request, ct);
        return CreatedAtRoute("getWorkItem", new { id = created.Id }, created);
    }

    /// <summary>Update fields of a work item (move state, edit description, reassign...).</summary>
    /// <remarks>
    /// Partial update: omitted properties are unchanged. Send <c>assignedTo: ""</c> to unassign
    /// and <c>description: ""</c> to clear the description. An optional <c>comment</c> is stored
    /// on the resulting history entry.
    /// </remarks>
    /// <param name="id">Work item id.</param>
    /// <param name="request">Fields to change.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPatch("{id:int}", Name = "updateWorkItem")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WorkItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkItemDto>> Update(int id, [FromBody] UpdateWorkItemRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    /// <summary>Add a discussion comment or automated test output to a work item.</summary>
    /// <param name="id">Work item id.</param>
    /// <param name="request">Comment text (markdown supported).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:int}/comments", Name = "addWorkItemComment")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WorkItemHistoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkItemHistoryDto>> AddComment(int id, [FromBody] AddCommentRequest request, CancellationToken ct)
    {
        var entry = await _service.AddCommentAsync(id, request, ct);
        return CreatedAtRoute("getWorkItem", new { id }, entry);
    }
}
