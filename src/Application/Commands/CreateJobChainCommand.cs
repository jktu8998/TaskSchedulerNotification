using Application.Dto;
using Application.Interfaces;

namespace Application.Commands;

/// <summary>Команда на создание новой цепочки заданий. Возвращает идентификатор созданной цепочки.</summary>
public sealed record CreateJobChainCommand(CreateJobChainRequest Request) : ICommand<Guid>, ITransactionalCommand;