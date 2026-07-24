
using System.Reflection;
using Dapper;
using Domain.Entities;
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
        
        // Кастомный маппинг для ScheduledTask: столбец "execution" -> свойство "Execution"
        // var taskMap = new CustomPropertyTypeMap(
        //     typeof(ScheduledTask),
        //     (type, columnName) =>
        //     {
        //         if (columnName.Equals("execution", StringComparison.OrdinalIgnoreCase))
        //             return type.GetProperty("Execution");
        //         // Для остальных столбцов оставляем стандартное поведение
        //         return type.GetProperty(columnName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        //     });
        // SqlMapper.SetTypeMap(typeof(ScheduledTask), taskMap);
        
        // TaskId ↔ Guid
        SqlMapper.AddTypeHandler(new TaskIdTypeHandler());
        SqlMapper.AddTypeHandler(new SenderIdTypeHandler());

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
        SqlMapper.AddTypeHandler(new ChainStepListTypeHandler());
        // Метаданные задания (TaskMetadata) — теперь тоже сохраняются в JSONB
        SqlMapper.AddTypeHandler(new SystemTextJsonbTypeHandler<TaskMetadata>());
    }
}