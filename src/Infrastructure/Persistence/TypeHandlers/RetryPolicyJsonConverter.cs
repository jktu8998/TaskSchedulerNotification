// Infrastructure/Persistence/TypeHandlers/RetryPolicyJsonConverter.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// Кастомный конвертер для <see cref="RetryPolicy"/>. 
/// При десериализации игнорирует лишние поля (MaxAttempts) и заполняет IntervalsSeconds.
/// При сериализации выводит только IntervalsSeconds (MaxAttempts исключён через JsonIgnore, 
/// но этот конвертер гарантирует, что даже если атрибут не сработает, лишнее не попадёт в JSON).
/// </summary>
public sealed class RetryPolicyJsonConverter : JsonConverter<RetryPolicy>
{
    public override RetryPolicy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        // Ручной парсинг объекта для извлечения только IntervalsSeconds
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("intervalsSeconds", out var intervalsProp))
            return RetryPolicy.Default; // или выбросить ошибку? Лучше значение по умолчанию.

        var intervals = new List<int>();
        foreach (var item in intervalsProp.EnumerateArray())
        {
            if (item.TryGetInt32(out int val))
                intervals.Add(val);
        }

        // Возвращаем новый объект через бизнес-конструктор (с валидацией)
        return new RetryPolicy(intervals);
    }

    public override void Write(Utf8JsonWriter writer, RetryPolicy value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("intervalsSeconds");
        JsonSerializer.Serialize(writer, value.IntervalsSeconds, options);
        writer.WriteEndObject();
    }
}