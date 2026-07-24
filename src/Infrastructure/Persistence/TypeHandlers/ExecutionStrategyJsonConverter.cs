using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Конвертер для полиморфной сериализации/десериализации ExecutionStrategy.
/// Определяет конкретный тип по свойству StrategyType.
/// </summary>
public sealed class ExecutionStrategyJsonConverter : JsonConverter<ExecutionStrategy>
{
    public override ExecutionStrategy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Ищем свойство с именем "strategyType" без учёта регистра
        JsonElement typeProp = default;
        bool found = false;
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "strategyType", StringComparison.OrdinalIgnoreCase))
            {
                typeProp = property.Value;
                found = true;
                break;
            }
        }
        if (!found)
            throw new JsonException("Missing required property 'strategyType'");

        var strategyType = typeProp.GetString();
        var json = root.GetRawText();

        return strategyType switch
        {
            "http" => JsonSerializer.Deserialize<HttpExecutionConfig>(json, options),
            // "grpc" => JsonSerializer.Deserialize<GrpcExecutionConfig>(json, options),
            _ => throw new JsonException($"Unknown execution strategy type: {strategyType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ExecutionStrategy value, JsonSerializerOptions options)
    {
        // Сериализуем конкретный тип, чтобы сохранить все свойства
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}