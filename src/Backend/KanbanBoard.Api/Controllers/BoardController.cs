using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanbanBoard.Api.Controllers;

/// <summary>Secured board API for AI agents (spec 4.2 GET /api/v1/board).</summary>
[ApiController]
[Route("api/v1/board")]
[Tags("Board")]
public sealed class BoardController : ControllerBase
{
    private readonly BoardService _board;

    public BoardController(BoardService board)
    {
        _board = board;
    }

    /// <summary>Get board metadata, columns, WIP limits and grouped work items.</summary>
    /// <remarks>
    /// <c>itemCount</c> and <c>isOverWipLimit</c> always reflect the whole board; the optional
    /// filters only narrow the <c>items</c> listed per column. Removed items are excluded.
    /// </remarks>
    /// <param name="filter">Optional filters: type, state, aiModified, assignedTo.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet(Name = "getBoard")]
    [ProducesResponseType(typeof(BoardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BoardDto>> Get([FromQuery] WorkItemQuery filter, CancellationToken ct)
        => Ok(await _board.GetBoardAsync(filter, ct));
}
