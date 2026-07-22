using Application.Commands;
using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace WebLayer.Controllers;

/// <summary>
/// Контроллер для работы с Dead Letter Queue.
/// </summary>
public class DlqController : ApiControllerBase
{
    private readonly IQueryHandler<GetDlqEntriesQuery, IReadOnlyList<DlqItemDto>> _getDlqHandler;
    private readonly ICommandHandler<RetryFromDlqCommand, Guid> _retryHandler;

    public DlqController(
        IQueryHandler<GetDlqEntriesQuery, IReadOnlyList<DlqItemDto>> getDlqHandler,
        ICommandHandler<RetryFromDlqCommand, Guid> retryHandler)
    {
        _getDlqHandler = getDlqHandler;
        _retryHandler = retryHandler;
    }

    /// <summary>
    /// Получить список записей Dead Letter Queue с пагинацией.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDlqEntries(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var query = new GetDlqEntriesQuery(skip, take);
        var entries = await _getDlqHandler.HandleAsync(query);
        return Ok(entries);
    }

    /// <summary>
    /// Повторить задание из Dead Letter Queue.
    /// </summary>
    [HttpPost("{id}/retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(long id)
    {
        var command = new RetryFromDlqCommand(id);
        var newTaskId = await _retryHandler.HandleAsync(command);
        return Ok(new { id = newTaskId });
    }
}