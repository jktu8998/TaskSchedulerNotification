using Application.Commands;
using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace WebLayer.Controllers;

/// <summary>
/// Контроллер управления заданиями (CRUD, пауза, возобновление).
/// </summary>
public class TasksController : ApiControllerBase
{
    private readonly ICommandHandler<CreateTaskCommand, Guid> _createHandler;
    private readonly ICommandHandler<CancelTaskCommand> _cancelHandler;
    private readonly ICommandHandler<PauseTaskCommand> _pauseHandler;
    private readonly ICommandHandler<ResumeTaskCommand> _resumeHandler;
    private readonly ICommandHandler<UpdateTaskCommand, Guid> _updateHandler;
    private readonly IQueryHandler<GetTasksQuery, IReadOnlyList<TaskResponse>> _getListHandler;
    private readonly IQueryHandler<GetTaskByIdQuery, TaskResponse> _getByIdHandler;

    public TasksController(
        ICommandHandler<CreateTaskCommand, Guid> createHandler,
        ICommandHandler<CancelTaskCommand> cancelHandler,
        ICommandHandler<PauseTaskCommand> pauseHandler,
        ICommandHandler<ResumeTaskCommand> resumeHandler,
        ICommandHandler<UpdateTaskCommand, Guid> updateHandler,
        IQueryHandler<GetTasksQuery, IReadOnlyList<TaskResponse>> getListHandler,
        IQueryHandler<GetTaskByIdQuery, TaskResponse> getByIdHandler)
    {
        _createHandler = createHandler;
        _cancelHandler = cancelHandler;
        _pauseHandler = pauseHandler;
        _resumeHandler = resumeHandler;
        _updateHandler = updateHandler;
        _getListHandler = getListHandler;
        _getByIdHandler = getByIdHandler;
    }

    /// <summary>
    /// Создать новое задание.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest request)
    {
        var command = new CreateTaskCommand(request);
        var taskId = await _createHandler.HandleAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = taskId }, new { id = taskId });
    }

    /// <summary>
    /// Получить список заданий с пагинацией и фильтрацией.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null)
    {
        var query = new GetTasksQuery(skip, take, status, type);
        var tasks = await _getListHandler.HandleAsync(query);
        return Ok(tasks);
    }

    /// <summary>
    /// Получить задание по идентификатору.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var query = new GetTaskByIdQuery(id);
        var task = await _getByIdHandler.HandleAsync(query);
        if (task is null) return NotFound();
        return Ok(task);
    }

    /// <summary>
    /// Отменить задание.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await _cancelHandler.HandleAsync(new CancelTaskCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Изменить задание (отмена старого и создание нового).
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateTaskRequest request)
    {
        var command = new UpdateTaskCommand(id, request);
        var newTaskId = await _updateHandler.HandleAsync(command);
        return Ok(new { id = newTaskId });
    }

    /// <summary>
    /// Приостановить задание.
    /// </summary>
    [HttpPost("{id}/pause")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Pause(Guid id)
    {
        await _pauseHandler.HandleAsync(new PauseTaskCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Возобновить задание.
    /// </summary>
    [HttpPost("{id}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resume(Guid id)
    {
        await _resumeHandler.HandleAsync(new ResumeTaskCommand(id));
        return NoContent();
    }
}