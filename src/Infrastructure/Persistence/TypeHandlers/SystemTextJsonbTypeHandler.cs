using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
// нужно потом удалить ссылки на Newtonsoft.Json из проекта
namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Dapper Type Handler на базе System.Text.Json для маппинга объектов в JSONB.
/// </summary>
public sealed class SystemTextJsonbTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new ExecutionStrategyJsonConverter(),
            new RetryPolicyJsonConverter(),
            new TaskMetadataJsonConverter() 
        }
    };

    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        if (parameter is NpgsqlParameter npgsqlParam)
            npgsqlParam.NpgsqlDbType = NpgsqlDbType.Jsonb;

        if (value == null)
        {
            parameter.Value = DBNull.Value;
        }
        else
        {
            parameter.Value = JsonSerializer.Serialize(value, Options);
        }
    }

    public override T Parse(object value)
    {
        if (value is null or DBNull)
            return default!;

        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json))
            return default!;

        return JsonSerializer.Deserialize<T>(json, Options)!;
    }
}