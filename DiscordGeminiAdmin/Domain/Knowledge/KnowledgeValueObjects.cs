using System.Security.Cryptography;
using System.Text;
using Vogen;

namespace DiscordGeminiAdmin.Domain.Knowledge;

/// <summary>Код правила или раздела знаний (например, rules#3, faq#1, precedent:0195...).</summary>
[ValueObject<string>]
public readonly partial struct RuleCode
{
    private static Validation Validate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 100
            ? Validation.Invalid("Код правила обязателен и не длиннее 100 символов")
            : Validation.Ok;
}

/// <summary>SHA-256 хэш содержимого документа для идемпотентной переиндексации.</summary>
[ValueObject<string>]
public readonly partial struct ContentHash
{
    private static Validation Validate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length != 64
            ? Validation.Invalid("ContentHash должен быть 64-символьной SHA-256 строкой")
            : Validation.Ok;

    public static ContentHash Compute(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return From(Convert.ToHexStringLower(bytes));
    }
}
