using System.Text;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;

namespace DiscordGeminiAdmin.Infrastructure.Discord;

internal sealed class NetCordDiscordNotifier(
    RestClient rest,
    ILogger<NetCordDiscordNotifier> logger) : IDiscordNotifier
{
    public async Task<Result> SendAutoBanAuditCardAsync(
        ChannelId modChannelId,
        ModerationCase moderationCase,
        CancellationToken ct)
    {
        try
        {
            var embed = new EmbedProperties
            {
                Title = "Автоматический бан (высокая уверенность ИИ)",
                Description = $"""
                    **Участник:** <@{moderationCase.MemberId.Value}> (`{moderationCase.MemberId.Value}`)
                    **Уверенность ИИ:** `{moderationCase.Assessment.Confidence.Value:P0}`
                    **Правило (RAG):** `{moderationCase.Assessment.Rule?.Value ?? "—"}`
                    **Сообщений-доказательств:** `{moderationCase.Assessment.EvidenceCount}`
                    **Причина:** {moderationCase.Assessment.Reason.Value}
                    """,
                Color = new Color(0xE74C3C)
            };

            var row = new ActionRowProperties([
                new ButtonProperties($"unban:{moderationCase.Id}", "Разбанить", ButtonStyle.Secondary)
            ]);

            await rest.SendMessageAsync(
                modChannelId.Value,
                new MessageProperties
                {
                    Embeds = [embed],
                    Components = [row]
                },
                cancellationToken: ct);

            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось отправить карточку авто-бана в канал {ChannelId}", modChannelId.Value);
            return Result.Fail("Не удалось опубликовать карточку авто-бана");
        }
    }

    public async Task<Result> SendHourlyDigestAsync(
        ChannelId modChannelId,
        HourlyDigest digest,
        CancellationToken ct)
    {
        try
        {
            // Ограничение Discord: не более 5 рядов кнопок на одно сообщение.
            foreach (var page in digest.Items.Chunk(5))
            {
                var sb = new StringBuilder();
                var rows = new List<IMessageComponentProperties>(page.Length);

                foreach (var item in page)
                {
                    var dryTag = item.WasDryRunAutoBan ? " `[DRY-RUN АВТО-БАН]`" : string.Empty;
                    sb.AppendLine($"• <@{item.MemberId.Value}> — **`{item.Confidence.Value:P0}`** (правило: `{item.RuleCode ?? "нет"}`){dryTag}");
                    sb.AppendLine($"  _{item.Reason}_");

                    rows.Add(new ActionRowProperties([
                        new ButtonProperties($"ban:{item.CaseId}", "Бан", ButtonStyle.Danger),
                        new ButtonProperties($"kick:{item.CaseId}", "Кик", ButtonStyle.Primary),
                        new ButtonProperties($"timeout:{item.CaseId}", "Таймаут 1ч", ButtonStyle.Secondary),
                        new ButtonProperties($"dismiss:{item.CaseId}", "Игнор", ButtonStyle.Success)
                    ]));
                }

                var embed = new EmbedProperties
                {
                    Title = $"Часовой дайджест ИИ-модератора ({digest.PeriodStart:HH:mm}–{digest.PeriodEnd:HH:mm} UTC)",
                    Description = sb.ToString().Trim(),
                    Color = new Color(0xF39C12)
                };

                await rest.SendMessageAsync(
                    modChannelId.Value,
                    new MessageProperties
                    {
                        Embeds = [embed],
                        Components = rows
                    },
                    cancellationToken: ct);
            }

            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось отправить часовой дайджест в канал {ChannelId}", modChannelId.Value);
            return Result.Fail("Не удалось отправить часовой дайджест в Discord");
        }
    }

    public async Task<Result> SendStructureProposalAsync(
        ChannelId modChannelId,
        StructureProposal proposal,
        CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(proposal.Summary);
            sb.AppendLine();
            sb.AppendLine("**Предлагаемые шаги:**");

            foreach (var change in proposal.Changes.Take(15))
            {
                var extra = !string.IsNullOrWhiteSpace(change.NewNameOrTopic)
                    ? $" → `{change.NewNameOrTopic}`"
                    : string.Empty;
                var cat = !string.IsNullOrWhiteSpace(change.ParentCategory)
                    ? $" (категория `{change.ParentCategory}`)"
                    : string.Empty;
                sb.AppendLine($"• **{change.Kind}** `{change.TargetName}`{cat}{extra} — {change.Rationale}");
            }

            var description = sb.ToString();
            if (description.Length > 4000)
                description = description[..4000];

            var embed = new EmbedProperties
            {
                Title = "План улучшения структуры сервера по тематике",
                Description = description,
                Color = new Color(0x3498DB)
            };

            var row = new ActionRowProperties([
                new ButtonProperties($"proposal-approve:{proposal.Id}", "Одобрить план", ButtonStyle.Success),
                new ButtonProperties($"proposal-reject:{proposal.Id}", "Отклонить", ButtonStyle.Danger)
            ]);

            await rest.SendMessageAsync(
                modChannelId.Value,
                new MessageProperties
                {
                    Embeds = [embed],
                    Components = [row]
                },
                cancellationToken: ct);

            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось отправить план структуры в канал {ChannelId}", modChannelId.Value);
            return Result.Fail("Не удалось отправить план структуры в Discord");
        }
    }
}
