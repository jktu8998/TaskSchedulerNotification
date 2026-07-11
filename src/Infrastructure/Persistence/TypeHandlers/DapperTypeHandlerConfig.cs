// Infrastructure/Persistence/TypeHandlers/DapperTypeHandlerConfig.cs

using Dapper;
using Domain.Enums;
using Domain.ValueObjects;
using TaskStatus = Domain.Enums.TaskStatus;

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
        SqlMapper.AddTypeHandler(new EnumTypeHandler<TaskStatus>());
        SqlMapper.AddTypeHandler(new EnumTypeHandler<TaskType>());
        SqlMapper.AddTypeHandler(new EnumTypeHandler<ResultDeliveryMode>());
        
        // Value Objects ↔ JSONB
        SqlMapper.AddTypeHandler(new JsonbTypeHandler<Schedule>());
        SqlMapper.AddTypeHandler(new JsonbTypeHandler<ExecutionConfig>());
        SqlMapper.AddTypeHandler(new JsonbTypeHandler<ResultDeliveryConfig>());
        SqlMapper.AddTypeHandler(new JsonbTypeHandler<PollingConfig>());
        SqlMapper.AddTypeHandler(new JsonbTypeHandler<RetryPolicy>());
    }
}