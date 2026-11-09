using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace DiscordGeminiAdmin.Presentation.Discord.Components;

/// <summary>
/// Единая проверка прав доступа уровня Модератор / Администратор для всех команд, кнопок и упоминаний бота.
/// </summary>
internal static class ModeratorGuard
{
    public const Permissions ModeratorPermissions =
        Permissions.Administrator |
        Permissions.ManageGuild |
        Permissions.ManageChannels |
        Permissions.ManageRoles |
        Permissions.BanUsers |
        Permissions.KickUsers |
        Permissions.ModerateUsers |
        Permissions.ManageMessages;

    private static readonly string[] ModeratorRoleKeywords =
    [
        "модер", "moder", "админ", "admin", "staff", "стафф", "куратор", "owner", "владелец"
    ];

    public static InteractionMessageProperties AccessDeniedCard() =>
        EmbedTheme.Card(
            "⛔ Доступ только для модераторов",
            "Пользоваться командами и возможностями бота могут только **модераторы** и **администраторы** сервера.",
            EmbedTheme.Danger,
            ephemeral: true);

    public static async ValueTask<bool> IsModeratorAsync(
        User user,
        Guild? cachedGuild,
        ulong? guildId,
        RestClient restClient)
    {
        if (cachedGuild is not null && cachedGuild.OwnerId == user.Id)
            return true;

        if (user is GuildInteractionUser interactionUser
            && (interactionUser.Permissions & ModeratorPermissions) != 0)
        {
            return true;
        }

        if (user is GuildUser guildUser && cachedGuild is not null)
        {
            foreach (var roleId in guildUser.RoleIds)
            {
                if (!cachedGuild.Roles.TryGetValue(roleId, out var role))
                    continue;

                if ((role.Permissions & ModeratorPermissions) != 0 || IsModeratorRoleName(role.Name))
                    return true;
            }

            return false;
        }

        var targetGuildId = cachedGuild?.Id ?? guildId;
        if (targetGuildId is null)
            return false;

        try
        {
            var restGuild = await restClient.GetGuildAsync(targetGuildId.Value);
            if (restGuild.OwnerId == user.Id)
                return true;

            var member = await restClient.GetGuildUserAsync(targetGuildId.Value, user.Id);
            foreach (var roleId in member.RoleIds)
            {
                if (!restGuild.Roles.TryGetValue(roleId, out var role))
                    continue;

                if ((role.Permissions & ModeratorPermissions) != 0 || IsModeratorRoleName(role.Name))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsModeratorRoleName(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        foreach (var keyword in ModeratorRoleKeywords)
        {
            if (roleName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
