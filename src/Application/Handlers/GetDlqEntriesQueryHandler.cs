using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Domain.Entities;
using Domain.Interfaces;

namespace Application.Handlers;

/// <summary>
/// Обработчик запроса записей DLQ.
/// Возвращает список снимков заданий, принадлежащих текущему отправителю.
/// </summary>
public sealed class GetDlqEntriesQueryHandler : IQueryHandler<GetDlqEntriesQuery, IReadOnlyList<DlqItemDto>>
{
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly IRequestContext _requestContext;

    public GetDlqEntriesQueryHandler(IDeadLetterRepository dlqRepo, IRequestContext requestContext)
    {
        _dlqRepo = dlqRepo;
        _requestContext = requestContext;
    }

    public async Task<IReadOnlyList<DlqItemDto>> HandleAsync(GetDlqEntriesQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DeadLetterEntry> entries;

        // Элегантная маршрутизация запросов к БД в зависимости от прав
        if (_requestContext.IsAdmin)
        {
            entries = await _dlqRepo.GetAllAsync(query.Skip, query.Take, cancellationToken);
        }
        else
        {
            entries = await _dlqRepo.GetBySenderIdAsync(_requestContext.SenderId, query.Skip, query.Take, cancellationToken);
        }

        return entries.Select(entry => new DlqItemDto
        {
            Id = entry.Id,
            TaskId = entry.TaskId.Value,
            SenderId = entry.SenderId, // Добавили вывод SenderId для удобства админа
            OriginalTaskSnapshot = entry.OriginalTaskSnapshot,
            ErrorDetails = entry.ErrorDetails,
            MovedAt = new DateTimeOffset(entry.MovedAt, TimeSpan.Zero)
        }).ToList().AsReadOnly();
    }
}