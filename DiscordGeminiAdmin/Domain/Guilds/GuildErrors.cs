using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Guilds;

public static class GuildErrors
{
    public static Error InvalidSettings(string reason) =>
        new Error(reason).WithErrorCode("G101");

    public static Error NotConfigured() =>
        new Error("Сервер ещё не настроен. Выполните /mod setup").WithErrorCode("G201");
}
