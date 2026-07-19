
using Dapper;
using Domain.Enums;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Выполняет регистрацию всех кастомных Type Handler'ов для Dapper.
/// Должен вызываться один раз при старте приложения.
/// </summary>
public static class DapperTypeHandlerConfig
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        // TaskId ↔ Guid
        SqlMapper.AddTypeHandler(new TaskIdTypeHandler());

        // Enums ↔ int
        SqlMapper.AddTypeHandler(new EnumTypeHandler<StatusTask>());
        SqlMapper.AddTypeHandler(new EnumTypeHandler<TaskType>());
        SqlMapper.AddTypeHandler(new EnumTypeHandler<ResultDeliveryMode>());
        
        // Value Objects ↔ JSONB
        SqlMapper.AddTypeHandler(new SystemTextJsonbTypeHandler<Schedule>());
        SqlMapper.AddTypeHandler(new SystemTextJsonbTypeHandler<ExecutionStrategy>());
        SqlMapper.AddTypeHandler(new SystemTextJsonbTypeHandler<ResultDeliveryConfig>());
        SqlMapper.AddTypeHandler(new SystemTextJsonbTypeHandler<PollingConfig>());
        SqlMapper.AddTypeHandler(new SystemTextJsonbTypeHandler<RetryPolicy>());
        // Метаданные задания (TaskMetadata) — теперь тоже сохраняются в JSONB
        SqlMapper.AddTypeHandler(new SystemTextJsonbTypeHandler<TaskMetadata>());
    }
}