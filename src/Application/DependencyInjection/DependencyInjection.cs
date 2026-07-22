using System.Reflection;
using Application.Commands;
using Application.DomainEventHandlers;
using Application.Dto;
using Application.Handlers;
using Application.Handlers.CrudHandlers;
using Application.Handlers.Query;
using Application.Interfaces;
using Application.Queries;
using Application.Services;
using Application.Validation;
using Domain.DomainEvents;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TaskFactory = Application.Services.TaskFactory;

namespace Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // ========== Фабрики ==========
        services.AddScoped<ITaskFactory, TaskFactory>();
        services.AddScoped<IChainTaskFactory, ChainTaskFactory>();

        // ========== Валидаторы (ручная регистрация) ==========
        services.AddScoped<IValidator<CreateTaskCommand>, CreateTaskCommandValidator>();
        services.AddScoped<IValidator<UpdateTaskCommand>, UpdateTaskCommandValidator>();
        services.AddScoped<IValidator<CreateJobChainCommand>, CreateJobChainCommandValidator>();
        // Валидаторы DTO (используются внутри)
        services.AddScoped<CreateTaskRequestValidator>();
        services.AddScoped<ScheduleDtoValidator>();
        services.AddScoped<ExecutionConfigDtoValidator>();
        services.AddScoped<CreateJobChainRequestValidator>();
        services.AddScoped<ChainStepDtoValidator>();

        // ========== Обработчики команд (CRUD) ==========
        services.AddScoped<ICommandHandler<CreateTaskCommand, Guid>, CreateTaskCommandHandler>();
        services.AddScoped<ICommandHandler<CancelTaskCommand>, CancelTaskCommandHandler>();
        services.AddScoped<ICommandHandler<PauseTaskCommand>, PauseTaskCommandHandler>();
        services.AddScoped<ICommandHandler<ResumeTaskCommand>, ResumeTaskCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateTaskCommand, Guid>, UpdateTaskCommandHandler>();
        services.AddScoped<ICommandHandler<RetryFromDlqCommand, Guid>, RetryFromDlqCommandHandler>();

        // Обработчики команд (фоновые)
        services.AddScoped<ICommandHandler<RunSchedulingCommand>, RunSchedulingCommandHandler>();
        services.AddScoped<ICommandHandler<RunHeartbeatCommand>, RunHeartbeatCommandHandler>();
        services.AddScoped<ICommandHandler<RunPollingCheckCommand>, RunPollingCheckCommandHandler>();
        services.AddScoped<ICommandHandler<RunExecutionCommand>, RunExecutionCommandHandler>();

        // Обработчики команд цепочек
        services.AddScoped<ICommandHandler<CreateJobChainCommand, Guid>, CreateJobChainCommandHandler>();
        services.AddScoped<ICommandHandler<PauseChainCommand>, PauseChainCommandHandler>();
        services.AddScoped<ICommandHandler<ResumeChainCommand>, ResumeChainCommandHandler>();
        services.AddScoped<ICommandHandler<CancelChainCommand>, CancelChainCommandHandler>();
        services.AddScoped<ICommandHandler<RunChainHeartbeatCommand>, RunChainHeartbeatCommandHandler>();

        // ========== Обработчики запросов ==========
        services.AddScoped<IQueryHandler<GetTasksQuery, IReadOnlyList<TaskResponse>>, GetTasksQueryHandler>();
        services.AddScoped<IQueryHandler<GetTaskByIdQuery, TaskResponse>, GetTaskByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetTaskLogsQuery, IReadOnlyList<TaskLogDto>>, GetTaskLogsQueryHandler>();
        services.AddScoped<IQueryHandler<GetDlqEntriesQuery, IReadOnlyList<DlqItemDto>>, GetDlqEntriesQueryHandler>();
        services.AddScoped<IQueryHandler<GetJobChainQuery, JobChainResponse>, GetJobChainQueryHandler>();
        services.AddScoped<IQueryHandler<GetJobChainsQuery, IReadOnlyList<JobChainListItemDto>>, GetJobChainsQueryHandler>();

        // ========== Доменные обработчики событий ==========
        // Универсальный логгер для всех событий (open generic)
        services.AddScoped(typeof(IDomainEventHandler<>), typeof(UniversalEventLogger<>));
        // Обработчики оркестрации цепочек
        services.AddScoped<IDomainEventHandler<TaskCompletedEvent>, ChainStepCompletionHandler>();
        services.AddScoped<IDomainEventHandler<TaskMovedToDlqEvent>, ChainStepFailureHandler>();

        return services;
    }
}