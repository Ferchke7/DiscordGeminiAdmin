using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using Vogen;

namespace DiscordGeminiAdmin.Infrastructure.Persistence;

[EfCoreConverter<GuildId>]
[EfCoreConverter<MemberId>]
[EfCoreConverter<ChannelId>]
[EfCoreConverter<MessageId>]
[EfCoreConverter<ServerTheme>]
[EfCoreConverter<RuleCode>]
[EfCoreConverter<ContentHash>]
[EfCoreConverter<Confidence>]
[EfCoreConverter<ModerationReason>]
[EfCoreConverter<RuleReference>]
internal partial class VogenEfCoreConverters;
