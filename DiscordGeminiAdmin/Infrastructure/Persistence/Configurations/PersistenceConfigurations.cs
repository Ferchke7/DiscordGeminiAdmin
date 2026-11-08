using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Infrastructure.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DiscordGeminiAdmin.Infrastructure.Persistence.Configurations;

public sealed class GuildProfileConfiguration : IEntityTypeConfiguration<GuildProfile>
{
    public void Configure(EntityTypeBuilder<GuildProfile> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GuildId)
            .HasConversion(new VogenEfCoreConverters.GuildIdEfCoreValueConverter());
        builder.Property(x => x.Theme)
            .HasConversion(new VogenEfCoreConverters.ServerThemeEfCoreValueConverter())
            .HasMaxLength(500);
        builder.Property(x => x.ModChannelId)
            .HasConversion(new VogenEfCoreConverters.ChannelIdEfCoreValueConverter());
        builder.ComplexProperty(x => x.ModerationPolicy);
        builder.HasIndex(x => x.GuildId).IsUnique();
    }
}

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GuildId)
            .HasConversion(new VogenEfCoreConverters.GuildIdEfCoreValueConverter());
        builder.Property(x => x.RuleCode)
            .HasConversion(new VogenEfCoreConverters.RuleCodeEfCoreValueConverter())
            .HasMaxLength(100);
        builder.Property(x => x.ContentHash)
            .HasConversion(new VogenEfCoreConverters.ContentHashEfCoreValueConverter())
            .HasMaxLength(64);
        builder.Property(x => x.Title).HasMaxLength(300);
        builder.HasIndex(x => new { x.GuildId, x.RuleCode }).IsUnique();
    }
}

public sealed class KnowledgeChunkRecordConfiguration : IEntityTypeConfiguration<KnowledgeChunkRecord>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunkRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GuildId)
            .HasConversion(new VogenEfCoreConverters.GuildIdEfCoreValueConverter());
        builder.Property(x => x.RuleCode).HasMaxLength(120);
        builder.Property(x => x.Title).HasMaxLength(300);
        builder.Property(x => x.Embedding).HasColumnType("vector(768)");
        builder.HasIndex(x => x.GuildId);
        builder.HasIndex(x => x.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");
    }
}

public sealed class BufferedMessageConfiguration : IEntityTypeConfiguration<BufferedMessage>
{
    public void Configure(EntityTypeBuilder<BufferedMessage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GuildId)
            .HasConversion(new VogenEfCoreConverters.GuildIdEfCoreValueConverter());
        builder.Property(x => x.ChannelId)
            .HasConversion(new VogenEfCoreConverters.ChannelIdEfCoreValueConverter());
        builder.Property(x => x.AuthorId)
            .HasConversion(new VogenEfCoreConverters.MemberIdEfCoreValueConverter());
        builder.Property(x => x.DiscordMessageId)
            .HasConversion(new VogenEfCoreConverters.MessageIdEfCoreValueConverter());
        builder.Property(x => x.AuthorName).HasMaxLength(100);
        builder.Property(x => x.Content).HasMaxLength(2000);
        builder.HasIndex(x => new { x.GuildId, x.CreatedAt });
    }
}

public sealed class ModerationCaseConfiguration : IEntityTypeConfiguration<ModerationCase>
{
    public void Configure(EntityTypeBuilder<ModerationCase> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.DomainEvents);
        builder.Property(x => x.GuildId)
            .HasConversion(new VogenEfCoreConverters.GuildIdEfCoreValueConverter());
        builder.Property(x => x.MemberId)
            .HasConversion(new VogenEfCoreConverters.MemberIdEfCoreValueConverter());
        builder.ComplexProperty(x => x.Assessment, a =>
        {
            a.Property(p => p.Confidence)
                .HasConversion(new VogenEfCoreConverters.ConfidenceEfCoreValueConverter());
            a.Property(p => p.Reason)
                .HasConversion(new VogenEfCoreConverters.ModerationReasonEfCoreValueConverter())
                .HasMaxLength(1000);
            a.Property(p => p.Rule)
                .HasConversion(new VogenEfCoreConverters.RuleReferenceEfCoreValueConverter())
                .HasMaxLength(200);
        });
        builder.HasIndex(x => new { x.GuildId, x.ResolvedAt });
    }
}

public sealed class HourlyDigestConfiguration : IEntityTypeConfiguration<HourlyDigest>
{
    public void Configure(EntityTypeBuilder<HourlyDigest> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.DomainEvents);
        builder.Property(x => x.GuildId)
            .HasConversion(new VogenEfCoreConverters.GuildIdEfCoreValueConverter());
        builder.OwnsMany(x => x.Items, b =>
        {
            b.ToJson();
            b.Property(i => i.MemberId)
                .HasConversion(new VogenEfCoreConverters.MemberIdEfCoreValueConverter());
            b.Property(i => i.Confidence)
                .HasConversion(new VogenEfCoreConverters.ConfidenceEfCoreValueConverter());
        });
    }
}

public sealed class StructureProposalConfiguration : IEntityTypeConfiguration<StructureProposal>
{
    public void Configure(EntityTypeBuilder<StructureProposal> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.DomainEvents);
        builder.Property(x => x.GuildId)
            .HasConversion(new VogenEfCoreConverters.GuildIdEfCoreValueConverter());
        builder.Property(x => x.Summary).HasMaxLength(2000);
        builder.OwnsMany(x => x.Changes, b => b.ToJson());
    }
}
