using Vogen;

namespace DiscordGeminiAdmin.Domain.Guilds;

/// <summary>Тематика Discord-сервера, задающая контекст для RAG и организатора.</summary>
[ValueObject<string>]
public readonly partial struct ServerTheme
{
    private static Validation Validate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 500
            ? Validation.Invalid("Тематика сервера обязательна и не длиннее 500 символов")
            : Validation.Ok;
}
