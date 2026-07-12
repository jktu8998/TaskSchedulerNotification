
using System.Data;
using Dapper;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Dapper Type Handler для маппинга TaskId ↔ uuid (Guid).
/// Позволяет Dapper автоматически конвертировать поля TaskId при чтении/записи.
/// </summary>
public sealed class TaskIdTypeHandler : SqlMapper.TypeHandler<TaskId>
{
    public override void SetValue(IDbDataParameter parameter, TaskId value)
    {
        parameter.Value = value.Value; // Guid
        parameter.DbType = DbType.Guid; // явно указываем тип
    }

    public override TaskId Parse(object value)
    {
        return value switch
        {
            Guid guid => TaskId.From(guid),
            string str when Guid.TryParse(str, out var guid) => TaskId.From(guid),
            _ => throw new ArgumentException($"Cannot convert {value} to TaskId")
        };
    }
}