
using System.Data;
using Dapper;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Универсальный Dapper Type Handler для enum'ов, хранящихся как integer.
/// </summary>
/// <typeparam name="T">Тип перечисления</typeparam>
public sealed class EnumTypeHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
{
    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.Value = Convert.ToInt32(value);
        parameter.DbType = DbType.Int32;
    }

    public override T Parse(object value)
    {
        return value switch
        {
            int i => (T)Enum.ToObject(typeof(T), i),
            string s when int.TryParse(s, out var i) => (T)Enum.ToObject(typeof(T), i),
            _ => throw new ArgumentException($"Cannot convert {value} to {typeof(T).Name}")
        };
    }
}