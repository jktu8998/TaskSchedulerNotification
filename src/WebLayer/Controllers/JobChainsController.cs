using Application.Commands;
using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace WebLayer.Controllers;

/// <summary>
/// Контроллер управления цепочками заданий (Job Chains).
/// </summary>
public class JobChainsController : ApiControllerBase
{
    private readonly ICommandHandler<CreateJobChainCommand, Guid> _createHandler;
    private readonly ICommandHandler<PauseChainCommand> _pauseHandler;
    private readonly ICommandHandler<ResumeChainCommand> _resumeHandler;
    private readonly ICommandHandler<CancelChainCommand> _cancelHandler;
    private readonly IQueryHandler<GetJobChainQuery, JobChainResponse> _getByIdHandler;
    private readonly IQueryHandler<GetJobChainsQuery, IReadOnlyList<JobChainListItemDto>> _getListHandler;

    public JobChainsController(
        ICommandHandler<CreateJobChainCommand, Guid> createHandler,
        ICommandHandler<PauseChainCommand> pauseHandler,
        ICommandHandler<ResumeChainCommand> resumeHandler,
        ICommandHandler<CancelChainCommand> cancelHandler,
        IQueryHandler<GetJobChainQuery, JobChainResponse> getByIdHandler,
        IQueryHandler<GetJobChainsQuery, IReadOnlyList<JobChainListItemDto>> getListHandler)
    {
        _createHandler = createHandler;
        _pauseHandler = pauseHandler;
        _resumeHandler = resumeHandler;
        _cancelHandler = cancelHandler;
        _getByIdHandler = getByIdHandler;
        _getListHandler = getListHandler;
    }

    /// <summary>
    /// Создать новую цепочку заданий.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateJobChainRequest request)
    {
        var command = new CreateJobChainCommand(request);
        var chainId = await _createHandler.HandleAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = chainId }, new { id = chainId });
    }

    /// <summary>
    /// Получить список цепочек с пагинацией и фильтрацией по статусу.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? status = null)
    {
        Domain.Enums.ChainStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<Domain.Enums.ChainStatus>(status, ignoreCase: true, out var parsed))
            statusFilter = parsed;

        var query = new GetJobChainsQuery(skip, take, statusFilter);
        var chains = await _getListHandler.HandleAsync(query);
        return Ok(chains);
    }

    /// <summary>
    /// Получить детальную информацию о цепочке.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var query = new GetJobChainQuery(id);
        var chain = await _getByIdHandler.HandleAsync(query);
        if (chain is null) return NotFound();
        return Ok(chain);
    }

    /// <summary>
    /// Приостановить цепочку.
    /// </summary>
    [HttpPost("{id}/pause")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pause(Guid id)
    {
        await _pauseHandler.HandleAsync(new PauseChainCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Возобновить цепочку.
    /// </summary>
    [HttpPost("{id}/resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resume(Guid id)
    {
        await _resumeHandler.HandleAsync(new ResumeChainCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Отменить цепочку.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await _cancelHandler.HandleAsync(new CancelChainCommand(id));
        return NoContent();
    }
}