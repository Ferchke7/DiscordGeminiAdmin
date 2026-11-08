using DiscordGeminiAdmin.Application.UseCases.Guilds.Setup;
using DiscordGeminiAdmin.Application.UseCases.Guilds.UpdateSettings;
using DiscordGeminiAdmin.Application.UseCases.Moderation.RunHourly;
using DiscordGeminiAdmin.Application.UseCases.Moderation.ScanMember;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Presentation.Discord.Mapper;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("mod", "Управление ИИ-модератором сервера", DefaultGuildPermissions = Permissions.ManageGuild)]
public sealed class ModerationCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("setup", "Первичная настройка тематики сервера и канала отчётов")]
    public async Task<InteractionMessageProperties> SetupAsync(
        [SlashCommandParameter(Description = "Тематика сервера (например: C# и .NET разработка)")] string theme,
        [SlashCommandParameter(Description = "Канал для отчётов и дайджестов модерации")] TextGuildChannel modChannel,
        [SlashCommandParameter(Description = "Dry-run режим (true = не банить реально, только писать в отчёт)")] bool dryRun = true)
    {
        if (Context.Guild is null)
            return Ephemeral("Команда доступна только на сервере.");

        var result = await bus.InvokeAsync<Result>(new SetupGuild(
            GuildId.From(Context.Guild.Id),
            theme,
            ChannelId.From(modChannel.Id),
            dryRun));

        return Ephemeral(result.IsSuccess
            ? $"Сервер настроен. Канал отчётов: <#{modChannel.Id}>, DryRun: `{dryRun}`."
            : FormatErrors(result));
    }

    [SubSlashCommand("settings", "Изменить пороги автоматического бана")]
    public async Task<InteractionMessageProperties> UpdateSettingsAsync(
        [SlashCommandParameter(Description = "Мин. уверенность ИИ для авто-бана (0.5..1.0)")] double? minConfidence = null,
        [SlashCommandParameter(Description = "Мин. сообщений-доказательств")] int? minEvidence = null,
        [SlashCommandParameter(Description = "Макс. авто-банов в час")] int? maxPerHour = null,
        [SlashCommandParameter(Description = "Режим DryRun (true/false)")] bool? dryRun = null)
    {
        if (Context.Guild is null)
            return Ephemeral("Команда доступна только на сервере.");

        var result = await bus.InvokeAsync<Result<ModerationPolicy>>(new UpdateGuildSettings(
            GuildId.From(Context.Guild.Id),
            minConfidence,
            minEvidence,
            maxPerHour,
            dryRun));

        if (result.IsFailed)
            return Ephemeral(FormatErrors(result));

        var p = result.Value;
        return Ephemeral(
            $"Настройки авто-бана: MinConfidence=`{p.MinConfidence:P0}`, MinEvidence=`{p.MinEvidenceMessages}`, MaxPerHour=`{p.MaxAutoBansPerHour}`, DryRun=`{p.DryRun}`.");
    }

    [SubSlashCommand("scan", "Внеочередная ИИ-проверка недавних сообщений участника")]
    public async Task ScanAsync(
        [SlashCommandParameter(Description = "Проверяемый участник")] User user)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(Ephemeral("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<ModerationCase?>>(new ScanMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(Ephemeral(FormatErrors(result)));
            return;
        }

        if (result.Value is null)
        {
            await FollowupAsync(Ephemeral($"У <@{user.Id}> не обнаружено нарушений за последние 24 часа."));
            return;
        }

        var dto = ModerationCaseOverviewMapper.ToDto(result.Value);
        await FollowupAsync(Ephemeral(
            $"Кейс `{dto.Id}` для <@{dto.MemberId.Value}>: уверенность `{dto.Assessment.Confidence.Value:P0}`, правило `{dto.Assessment.Rule?.Value ?? "нет"}` — {dto.Assessment.Reason.Value}"));
    }

    [SubSlashCommand("digest-now", "Запустить часовой прогон ИИ-модерации прямо сейчас")]
    public async Task DigestNowAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(Ephemeral("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<RunHourlyModerationSummary>>(
            new RunHourlyModeration(GuildId.From(Context.Guild.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(Ephemeral(FormatErrors(result)));
            return;
        }

        var s = result.Value;
        await FollowupAsync(Ephemeral(
            $"Часовой прогон завершён. Открыто кейсов: `{s.CasesOpened}`, авто-банов: `{s.AutoBannedCount}`, дайджест: `{s.DigestId?.ToString() ?? "не требовался"}`."));
    }

    private static InteractionMessageProperties Ephemeral(string text) =>
        new() { Content = text, Flags = MessageFlags.Ephemeral };

    private static string FormatErrors(ResultBase result) =>
        string.Join("; ", result.Errors.Select(e => e.Message));
}
