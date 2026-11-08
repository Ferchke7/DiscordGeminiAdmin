using Vogen;

namespace DiscordGeminiAdmin.Domain.Moderation;

/// <summary>Уверенность ИИ в оценке нарушения, от 0 до 1 включительно.</summary>
[ValueObject<double>]
public readonly partial struct Confidence
{
    private static Validation Validate(double value) =>
        double.IsNaN(value) || value is < 0 or > 1
            ? Validation.Invalid("Уверенность должна быть в диапазоне 0..1")
            : Validation.Ok;
}

/// <summary>Обоснование решения модерации (от ИИ или человека).</summary>
[ValueObject<string>]
public readonly partial struct ModerationReason
{
    private static Validation Validate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 1000
            ? Validation.Invalid("Причина обязательна и не длиннее 1000 символов")
            : Validation.Ok;
}

/// <summary>Ссылка на конкретный код правила сервера из RAG (например, rules#3).</summary>
[ValueObject<string>]
public readonly partial struct RuleReference
{
    private static Validation Validate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 200
            ? Validation.Invalid("Ссылка на правило обязательна и не длиннее 200 символов")
            : Validation.Ok;
}
