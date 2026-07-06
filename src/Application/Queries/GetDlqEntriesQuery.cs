using System.Collections.Generic;
using Application.Dto;

namespace Application.Queries;

/// <summary>
/// Запрос на получение записей Dead Letter Queue с пагинацией.
/// </summary>
public sealed record GetDlqEntriesQuery(int Skip, int Take) : IQuery<IReadOnlyList<DlqItemDto>>;