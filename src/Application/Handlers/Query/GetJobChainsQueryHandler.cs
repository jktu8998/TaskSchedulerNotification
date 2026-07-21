using Application.Dto;
using Application.Interfaces;
using Application.Queries;
using Domain.Interfaces;

namespace Application.Handlers;

/// <summary>
/// Обработчик запроса списка цепочек заданий.
/// Возвращает краткую информацию с пагинацией и опциональной фильтрацией по статусу.
/// Результаты автоматически ограничены текущим отправителем.
/// </summary>
public sealed class GetJobChainsQueryHandler : IQueryHandler<GetJobChainsQuery, IReadOnlyList<JobChainListItemDto>>
{
    private readonly IJobChainRepository _chainRepo;
    private readonly IRequestContext _requestContext;

    public GetJobChainsQueryHandler(IJobChainRepository chainRepo, IRequestContext requestContext)
    {
        _chainRepo = chainRepo;
        _requestContext = requestContext;
    }

    public async Task<IReadOnlyList<JobChainListItemDto>> HandleAsync(GetJobChainsQuery query, CancellationToken cancellationToken = default)
    {
        var chains = await _chainRepo.GetBySenderIdAsync(
            _requestContext.SenderId,
            query.Skip,
            query.Take,
            query.Status,
            cancellationToken);

        return chains.Select(chain => new JobChainListItemDto
        {
            Id = chain.Id.Value,
            SenderId = chain.SenderId.ToString(),
            Name = chain.Name,
            Status = chain.Status.ToString(),
            StepCount = chain.Steps.Length,
            CreatedAt = new DateTimeOffset(chain.CreatedAt, TimeSpan.Zero)
        }).ToList().AsReadOnly();
    }
}