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
    TimeProvider timeProvider,
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
                Title = "🛡️ Автоматический бан ИИ-модератором",
                Description = $"Высокая уверенность в нарушении правил сервера. Участник заблокирован.",
                Color = new Color(0xE74C3C),
                Timestamp = timeProvider.GetUtcNow(),
                Fields =
                [
                    new EmbedFieldProperties { Name = "👤 Участник", Value = $"<@{moderationCase.MemberId.Value}>\n`{moderationCase.MemberId.Value}`", Inline = true },
                    new EmbedFieldProperties { Name = "🎯 Уверенность ИИ", Value = $"`{moderationCase.Assessment.Confidence.Value:P0}`", Inline = true },
                    new EmbedFieldProperties { Name = "📜 Правило (RAG)", Value = $"`{moderationCase.Assessment.Rule?.Value ?? "—"}` (`{moderationCase.Assessment.EvidenceCount}` сообщ.)", Inline = true },
                    new EmbedFieldProperties { Name = "📝 Обоснование Gemini", Value = moderationCase.Assessment.Reason.Value, Inline = false }
                ],
                Footer = new EmbedFooterProperties { Text = $"ID кейса: {moderationCase.Id} • Gemini AI Moderator" }
            };

            var row = new ActionRowProperties([
                new ButtonProperties($"unban:{moderationCase.Id}", "🔓 Разбанить и записать прецедент", ButtonStyle.Secondary)
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
            foreach (var page in digest.Items.Chunk(5))
            {
                var sb = new StringBuilder();
                var rows = new List<IMessageComponentProperties>(page.Length);

                foreach (var item in page)
                {
                    var dryTag = item.WasDryRunAutoBan ? " `🧪 [DRY-RUN АВТО-БАН]`" : string.Empty;
                    sb.AppendLine($"### ⚠️ <@{item.MemberId.Value}> — уверенность `{item.Confidence.Value:P0}`");
                    sb.AppendLine($"> **Правило:** `{item.RuleCode ?? "не указано"}`{dryTag}");
                    sb.AppendLine($"> **Вердикт ИИ:** {item.Reason}");
                    sb.AppendLine();

                    rows.Add(new ActionRowProperties([
                        new ButtonProperties($"ban:{item.CaseId}", "🔨 Бан", ButtonStyle.Danger),
                        new ButtonProperties($"kick:{item.CaseId}", "👢 Кик", ButtonStyle.Primary),
                        new ButtonProperties($"timeout:{item.CaseId}", "🔇 Таймаут 1ч", ButtonStyle.Secondary),
                        new ButtonProperties($"dismiss:{item.CaseId}", "✅ Игнор", ButtonStyle.Success)
                    ]));
                }

                var embed = new EmbedProperties
                {
                    Title = $"📊 Часовой дайджест ИИ-модератора ({digest.PeriodStart:HH:mm}–{digest.PeriodEnd:HH:mm} UTC)",
                    Description = sb.ToString().Trim(),
                    Color = new Color(0xF39C12),
                    Timestamp = timeProvider.GetUtcNow(),
                    Footer = new EmbedFooterProperties { Text = "Нажмите кнопку под участником для применения действия и обучения RAG" }
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
            sb.AppendLine($"**Резюме ИИ-архитектора:**\n{proposal.Summary}");
            sb.AppendLine();
            sb.AppendLine("**📋 Предлагаемые изменения (`" + proposal.Changes.Count + "`):**");

            foreach (var change in proposal.Changes.Take(15))
            {
                var icon = change.Kind switch
                {
                    ProposedChangeKind.CreateCategory => "📁",
                    ProposedChangeKind.CreateTextChannel => "💬",
                    ProposedChangeKind.CreateVoiceChannel => "🔊",
                    ProposedChangeKind.UpdateChannelTopic => "📝",
                    ProposedChangeKind.RenameChannel => "✏️",
                    ProposedChangeKind.CreateRole => "🎭",
                    _ => "•"
                };

                var extra = !string.IsNullOrWhiteSpace(change.NewNameOrTopic)
                    ? $" → `{change.NewNameOrTopic}`"
                    : string.Empty;
                var cat = !string.IsNullOrWhiteSpace(change.ParentCategory)
                    ? $" _(в `{change.ParentCategory}`)_"
                    : string.Empty;
                sb.AppendLine($"{icon} **{change.Kind}** `{change.TargetName}`{cat}{extra}\n  ↳ _{change.Rationale}_");
            }

            var description = sb.ToString();
            if (description.Length > 4000)
                description = description[..4000];

            var embed = new EmbedProperties
            {
                Title = "🏗️ План улучшения структуры сервера по тематике",
                Description = description,
                Color = new Color(0x3498DB),
                Timestamp = timeProvider.GetUtcNow(),
                Footer = new EmbedFooterProperties { Text = $"ID плана: {proposal.Id} • Изменения применятся только после нажатия кнопки" }
            };

            var row = new ActionRowProperties([
                new ButtonProperties($"proposal-approve:{proposal.Id}", "✅ Одобрить и применить", ButtonStyle.Success),
                new ButtonProperties($"proposal-reject:{proposal.Id}", "✖️ Отклонить", ButtonStyle.Danger)
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

    public async Task<Result> SendAdminActionLogAsync(
        ChannelId modChannelId,
        string actionTitle,
        MemberId? targetMemberId,
        MemberId moderatorId,
        string reason,
        uint colorHex,
        Guid? unbanCaseId,
        CancellationToken ct)
    {
        try
        {
            var fields = new List<EmbedFieldProperties>
            {
                new() { Name = "🛡️ Модератор", Value = $"<@{moderatorId.Value}>", Inline = true }
            };

            if (targetMemberId is not null)
            {
                fields.Add(new EmbedFieldProperties
                {
                    Name = "👤 Целевой участник",
                    Value = $"<@{targetMemberId.Value.Value}> (`{targetMemberId.Value.Value}`)",
                    Inline = true
                });
            }

            fields.Add(new EmbedFieldProperties
            {
                Name = "📋 Причина / Детали",
                Value = reason,
                Inline = false
            });

            var embed = new EmbedProperties
            {
                Title = actionTitle,
                Color = new Color((int)colorHex),
                Timestamp = timeProvider.GetUtcNow(),
                Fields = fields,
                Footer = new EmbedFooterProperties { Text = "Журнал действий администрации • Gemini AI Moderator" }
            };

            var props = new MessageProperties { Embeds = [embed] };
            if (unbanCaseId.HasValue)
            {
                props.Components =
                [
                    new ActionRowProperties([
                        new ButtonProperties($"unban:{unbanCaseId.Value}", "🔓 Разбанить", ButtonStyle.Secondary)
                    ])
                ];
            }

            await rest.SendMessageAsync(modChannelId.Value, props, cancellationToken: ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось отправить лог админ-действия в канал {ChannelId}", modChannelId.Value);
            return Result.Fail("Не удалось записать событие в мод-канал");
        }
    }
}
