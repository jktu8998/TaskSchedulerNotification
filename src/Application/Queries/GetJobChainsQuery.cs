using Application.Dto;
using Domain.Enums;

namespace Application.Queries;

/// <summary>Запрос на получение списка цепочек с фильтрацией по статусу и пагинацией.</summary>
public sealed record GetJobChainsQuery(
    int Skip,
    int Take,
    ChainStatus? Status = null
) : IQuery<IReadOnlyList<JobChainListItemDto>>;