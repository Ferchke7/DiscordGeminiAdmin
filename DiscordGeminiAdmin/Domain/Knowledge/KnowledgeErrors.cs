using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Knowledge;

public enum KnowledgeSourceType
{
    Rules,
    ThemeAndFaq,
    ChannelGuide,
    Precedent
}

public static class KnowledgeErrors
{
    public static int MaxContentLength => 20_000;

    public static Error DocumentTooLarge =>
        new Error($"Документ не может превышать {MaxContentLength} символов").WithErrorCode("K101");

    public static Error EmptyDocument =>
        new Error("Содержимое документа не может быть пустым").WithErrorCode("K102");

    public static Error DocumentNotFound() =>
        new Error("Документ базы знаний не найден").WithErrorCode("K201");
}
