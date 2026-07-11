// Infrastructure/Persistence/TypeHandlers/JsonbTypeHandler.cs  

using System;
using System.Data;
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>  
/// Dapper Type Handler для сериализации/десериализации объектов в колонки PostgreSQL JSONB.  
/// </summary>  
/// <typeparam name="T">Тип объекта для хранения в JSONB</typeparam>  
public sealed class JsonbTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    // Настройки сериализатора, чтобы JSON в базе был красивым (camelCase)
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        // 1. Критично для PostgreSQL: явно указываем, что это JSONB, а не обычный text
        if (parameter is NpgsqlParameter npgsqlParameter)
        {
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
        }

        if (value == null)
        {
            parameter.Value = DBNull.Value;
        }
        else
        {
            // 2. Используем Newtonsoft.Json, чтобы работали наши приватные конструкторы в домене
            parameter.Value = JsonConvert.SerializeObject(value, Settings);
        }
    }

    public override T Parse(object value)
    {
        if (value is null or DBNull)
            return default!;

        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json))
            return default!;

        return JsonConvert.DeserializeObject<T>(json, Settings)!;
    }
}