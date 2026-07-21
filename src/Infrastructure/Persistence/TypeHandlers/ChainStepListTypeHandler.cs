using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Domain.ValueObjects;
using Npgsql;
using NpgsqlTypes;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Dapper Type Handler для маппинга ImmutableArray&lt;ChainStep&gt; ↔ jsonb.
/// Использует System.Text.Json с поддержкой полиморфного ExecutionStrategy.
/// </summary>
public sealed class ChainStepListTypeHandler : SqlMapper.TypeHandler<ImmutableArray<ChainStep>>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ExecutionStrategyJsonConverter() }
    };

    public override void SetValue(IDbDataParameter parameter, ImmutableArray<ChainStep> value)
    {
        if (parameter is NpgsqlParameter npgsqlParam)
            npgsqlParam.NpgsqlDbType = NpgsqlDbType.Jsonb;

        var json = JsonSerializer.Serialize(value, Options);
        parameter.Value = json;
    }

    public override ImmutableArray<ChainStep> Parse(object value)
    {
        if (value is null or DBNull)
            return ImmutableArray<ChainStep>.Empty;

        var json = value.ToString()!;
        if (string.IsNullOrWhiteSpace(json))
            return ImmutableArray<ChainStep>.Empty;

        var list = JsonSerializer.Deserialize<List<ChainStep>>(json, Options);
        return list?.ToImmutableArray() ?? ImmutableArray<ChainStep>.Empty;
    }
}