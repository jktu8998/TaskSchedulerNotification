using Application.Dto;

namespace Application.Queries;

/// <summary>Запрос на получение информации о цепочке по её идентификатору.</summary>
public sealed record GetJobChainQuery(Guid ChainId) : IQuery<JobChainResponse>;