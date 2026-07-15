using Application.Dto;
using FluentValidation;

namespace Application.Validation;

/// <summary>
/// Полиморфный валидатор конфигурации выполнения.
/// Правила зависят от указанного ExecutionType.
/// </summary>
public sealed class ExecutionConfigDtoValidator : AbstractValidator<ExecutionConfigDto>
{
    // Разрешенные HTTP-методы (дублируем, чтобы не зависеть от домена)
    private static readonly HashSet<string> AllowedHttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"
    };

    public ExecutionConfigDtoValidator()
    {
        // ExecutionType обязателен, по умолчанию "http" будет работать, но здесь строго проверяем
        RuleFor(x => x.ExecutionType)
            .NotEmpty()
            .WithMessage("ExecutionType is required.");

        // Полиморфная валидация на основе ExecutionType
        When(x => IsHttp(x.ExecutionType), () =>
        {
            ApplyHttpRules();
        });

        // Для gRPC (пока заглушка с ошибкой) — закомментировано, чтобы не блокировать неизвестный тип
        // Когда добавим GrpcExecutionConfig, раскомментируем:
        // When(x => IsGrpc(x.ExecutionType), () => { ApplyGrpcRules(); });

        // Для неизвестных типов — ошибка (если не HTTP и не gRPC)
        When(x => !IsHttp(x.ExecutionType) /* && !IsGrpc(x.ExecutionType) */, () =>
        {
            RuleFor(x => x.ExecutionType)
                .Must(_ => false)
                .WithMessage(dto => $"Unsupported execution type: {dto.ExecutionType}.");
        });
    }

    private static bool IsHttp(string? type) =>
        string.Equals(type ?? "http", "http", StringComparison.OrdinalIgnoreCase);

    // private static bool IsGrpc(string? type) =>
    //     string.Equals(type, "grpc", StringComparison.OrdinalIgnoreCase);

    private void ApplyHttpRules()
    {
        RuleFor(x => x.Method)
            .NotEmpty().WithMessage("HTTP method is required.")
            .Must(method => AllowedHttpMethods.Contains(method!))
            .WithMessage($"HTTP method must be one of: {string.Join(", ", AllowedHttpMethods)}.");

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL is required.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                         (uri.Scheme == "http" || uri.Scheme == "https"))
            .WithMessage("URL must be a valid absolute HTTP/HTTPS URL.");

        // Заголовки: если заданы, проверяем ключи
        When(x => x.Headers is { Count: > 0 }, () =>
        {
            RuleForEach(x => x.Headers)
                .Must(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                .WithMessage("Header keys cannot be empty.");
        });

        // TimeoutSeconds должен быть > 0, если задан
        RuleFor(x => x.TimeoutSeconds)
            .GreaterThan(0)
            .When(x => x.TimeoutSeconds.HasValue)
            .WithMessage("Timeout must be greater than 0.");
    }
}