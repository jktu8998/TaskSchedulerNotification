namespace Domain.ValueObjects;

/// <summary>
/// Идентификатор сервиса-отправителя задания.
/// Обёртка над строкой с гарантией непустоты и соответствия формату (kebab-case).
/// Сравнивается по значению строки с Ordinal семантикой.
/// </summary>
public readonly record struct SenderId
{
    public string Value { get; }

    /// <summary>
    /// Создаёт SenderId.
    /// </summary>
    /// <param name="value">Строка идентификатора. Должна быть непустой, соответствовать формату.</param>
    /// <exception cref="ArgumentException">Если value null, пусто или нарушает формат.</exception>
    public SenderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SenderId cannot be null or empty.", nameof(value));

        // Допустимый формат: kebab-case,  если вдруг понадобится какой то явный формат доработаю это 
        /*if (value.Length < 2 || value.Length > 63)
            throw new ArgumentException("SenderId must be between 2 and 63 characters.", nameof(value));

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!(char.IsLower(c) || char.IsDigit(c) || c == '-'))
                throw new ArgumentException(
                    $"SenderId must be lowercase alphanumeric or hyphen. Invalid character '{c}' at position {i}.", nameof(value));
        }

        if (value[0] == '-' || value[^1] == '-')
            throw new ArgumentException("SenderId cannot start or end with a hyphen.", nameof(value));*/

        Value = value;
    }

    /// <summary>
    /// Неявное приведение из строки (удобно при вызове методов, но с осторожностью).
    /// </summary>
    public static implicit operator SenderId(string value) => new(value);

    /// <summary>
    /// Явное приведение к строке.
    /// </summary>
    public static explicit operator string(SenderId id) => id.Value;

    public override string ToString() => Value;
}