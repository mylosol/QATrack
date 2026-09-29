using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanbanBoard.Api.Controllers;

/// <summary>
/// Unauthenticated endpoints used by the browser SPA (spec 5.1 "Zero-authentication access").
/// </summary>
/// <remarks>
/// These mirror the AI API but are recorded as human actions. They are hidden
/// from the OpenAPI document (which targets AI tool calling) and protected
/// against cross-site request forgery by <c>UiRequestGuardMiddleware</c>,
/// which requires a custom header on every mutating request.
/// </remarks>
[ApiController]
[Route("api/ui")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class UiBoardController : ControllerBase
{
    private readonly WorkItemService _items;
    private readonly BoardService _board;

    public UiBoardController(WorkItemService items, BoardService board)
    {
        _items = items;
        _board = board;
    }

    /// <summary>Board with columns, WIP status and filtered cards.</summary>
    [HttpGet("board")]
    public async Task<ActionResult<BoardDto>> GetBoard([FromQuery] WorkItemQuery filter, CancellationToken ct)
        => Ok(await _board.GetBoardAsync(filter, ct));

    /// <summary>Single work item with history (card modal).</summary>
    [HttpGet("workitems/{id:int}")]
    public async Task<ActionResult<WorkItemDto>> Get(int id, CancellationToken ct)
        => Ok(await _items.GetAsync(id, ct));

    /// <summary>Create a work item from the "New work item" dialog.</summary>
    [HttpPost("workitems")]
    public async Task<ActionResult<WorkItemDto>> Create([FromBody] CreateWorkItemRequest request, CancellationToken ct)
    {
        var created = await _items.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Edit fields or move a card (drag-and-drop / keyboard move).</summary>
    [HttpPatch("workitems/{id:int}")]
    public async Task<ActionResult<WorkItemDto>> Update(int id, [FromBody] UpdateWorkItemRequest request, CancellationToken ct)
        => Ok(await _items.UpdateAsync(id, request, ct));

    /// <summary>Add a discussion comment.</summary>
    [HttpPost("workitems/{id:int}/comments")]
    public async Task<ActionResult<WorkItemHistoryDto>> AddComment(int id, [FromBody] AddCommentRequest request, CancellationToken ct)
    {
        var entry = await _items.AddCommentAsync(id, request, ct);
        return CreatedAtAction(nameof(Get), new { id }, entry);
    }
}
