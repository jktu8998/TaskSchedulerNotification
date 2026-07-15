using Application.Commands;
using Application.Handlers;
using Application.Interfaces;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Decorator;

/// <summary>
/// Декоратор, выполняющий валидацию команды перед вызовом вложенного хендлера.
/// Если валидатор не зарегистрирован в DI, команда передаётся без проверки.
/// </summary>
public sealed class ValidationCommandHandlerDecorator<TCommand> : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    private readonly ICommandHandler<TCommand> _inner;
    private readonly IServiceProvider _serviceProvider;

    public ValidationCommandHandlerDecorator(
        ICommandHandler<TCommand> inner,
        IServiceProvider serviceProvider)
    {
        _inner = inner;
        _serviceProvider = serviceProvider;
    }

    public async Task HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var validator = _serviceProvider.GetService<IValidator<TCommand>>();
        if (validator is not null)
        {
            var result = await validator.ValidateAsync(command, cancellationToken);
            if (!result.IsValid)
                throw new ValidationException(result.Errors);
        }

        await _inner.HandleAsync(command, cancellationToken);
    }
}
 

/// <summary>
/// Декоратор, выполняющий валидацию команды перед вызовом вложенного хендлера (с возвратом результата).
/// </summary>
public sealed class ValidationCommandHandlerDecorator<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner;
    private readonly IServiceProvider _serviceProvider;

    public ValidationCommandHandlerDecorator(
        ICommandHandler<TCommand, TResult> inner,
        IServiceProvider serviceProvider)
    {
        _inner = inner;
        _serviceProvider = serviceProvider;
    }

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var validator = _serviceProvider.GetService<IValidator<TCommand>>();
        if (validator is not null)
        {
            var result = await validator.ValidateAsync(command, cancellationToken);
            if (!result.IsValid)
                throw new ValidationException(result.Errors);
        }

        return await _inner.HandleAsync(command, cancellationToken);
    }
}