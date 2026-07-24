// Infrastructure/Persistence/TypeHandlers/TaskMetadataJsonConverter.cs
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.ValueObjects;

namespace Infrastructure.Persistence.TypeHandlers;

public sealed class TaskMetadataJsonConverter : JsonConverter<TaskMetadata>
{
    public override TaskMetadata? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        // Разбираем весь JSON-объект
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Если есть свойство "Data" – десериализуем его в словарь, иначе – пустой словарь
        Dictionary<string, string>? data = null;
        if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
        {
            data = new Dictionary<string, string>();
            foreach (var property in dataElement.EnumerateObject())
            {
                // Значения могут быть строками или другими примитивами – приводим к строке
                string? val = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
                if (val != null)
                    data[property.Name] = val;
            }
        }

        // Создаём через бизнес-конструктор (он принимает IReadOnlyDictionary)
        return new TaskMetadata(data ?? new Dictionary<string, string>());
    }

    public override void Write(Utf8JsonWriter writer, TaskMetadata value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("data");
        JsonSerializer.Serialize(writer, value.Data, options); // словарь сериализуется как объект
        writer.WriteEndObject();
    }
}