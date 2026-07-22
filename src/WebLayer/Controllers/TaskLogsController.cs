using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace WebLayer.Controllers;

/// <summary>
/// Контроллер для получения логов заданий.
/// </summary>
public class TaskLogsController : ApiControllerBase
{
    private readonly IQueryHandler<GetTaskLogsQuery, IReadOnlyList<TaskLogDto>> _getLogsHandler;

    public TaskLogsController(
        IQueryHandler<GetTaskLogsQuery, IReadOnlyList<TaskLogDto>> getLogsHandler)
    {
        _getLogsHandler = getLogsHandler;
    }

    /// <summary>
    /// Получить логи по идентификатору задания.
    /// </summary>
    [HttpGet("{taskId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLogs(Guid taskId)
    {
        var query = new GetTaskLogsQuery(taskId);
        var logs = await _getLogsHandler.HandleAsync(query);
        if (logs.Count == 0) return NotFound();
        return Ok(logs);
    }
}