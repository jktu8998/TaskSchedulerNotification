// Infrastructure/Persistence/TypeHandlers/SenderIdTypeHandler.cs
using Dapper;
using Domain.ValueObjects;
using System.Data;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Dapper Type Handler для маппинга SenderId ↔ text (string).
/// Позволяет передавать SenderId как параметр запроса и читать из БД.
/// </summary>
public sealed class SenderIdTypeHandler : SqlMapper.TypeHandler<SenderId>
{
    public override void SetValue(IDbDataParameter parameter, SenderId value)
    {
        parameter.Value = value.Value; // просто строка
        parameter.DbType = DbType.String;
    }

    public override SenderId Parse(object value)
    {
        return value switch
        {
            string s => new SenderId(s),
            _ => throw new ArgumentException($"Cannot convert {value} to SenderId")
        };
    }
}