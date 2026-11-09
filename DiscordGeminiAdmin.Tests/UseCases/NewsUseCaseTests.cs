using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.UseCases.News.PublishNews;
using DiscordGeminiAdmin.Application.UseCases.News.SetupNews;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Infrastructure.Ai.Gemini;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DiscordGeminiAdmin.Tests.UseCases;

public sealed class NewsUseCaseTests
{
    private readonly INewsGeneratorAi _newsAi = Substitute.For<INewsGeneratorAi>();
    private readonly IGuildGateway _gateway = Substitute.For<IGuildGateway>();
    private readonly IGuildProfileRepository _guilds = Substitute.For<IGuildProfileRepository>();

    private readonly GuildId _guildId = GuildId.From(123456);
    private readonly ChannelId _newsChannelId = ChannelId.From(789012);

    [Fact]
    public void ParseDigestJson_ValidJson_ReturnsSuccess()
    {
        var json = """
            {
              "headline": "📰 Главные IT-новости",
              "introduction": "Обзор ключевых событий индустрии.",
              "items": [
                {
                  "title": "🚀 Релиз .NET 10",
                  "summary": "Microsoft выпустила .NET 10 с улучшенной производительностью.",
                  "significance": "Ускоряет запуск микросервисов.",
                  "sourceUrl": "https://devblogs.microsoft.com/dotnet"
                }
              ],
              "discussionQuestion": "Что думаете о новом релизе?"
            }
            """;

        var result = GeminiNewsAi.ParseDigestJson(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Headline.ShouldBe("📰 Главные IT-новости");
        result.Value.Items.Count.ShouldBe(1);
        result.Value.Items[0].Title.ShouldBe("🚀 Релиз .NET 10");
        result.Value.DiscussionQuestion.ShouldBe("Что думаете о новом релизе?");
    }

    [Fact]
    public void ParseDigestJson_MarkdownCodeFences_ReturnsSuccess()
    {
        var json = """
            ```json
            {
              "headline": "🤖 Новости ИИ",
              "introduction": "Главное из мира нейросетей.",
              "items": [
                {
                  "title": "🧠 Gemini 3.8",
                  "summary": "Новая модель рассуждений.",
                  "significance": "Более точные ответы.",
                  "sourceUrl": null
                }
              ],
              "discussionQuestion": "Используете ли вы ИИ в работе?"
            }
            ```
            """;

        var result = GeminiNewsAi.ParseDigestJson(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Headline.ShouldBe("🤖 Новости ИИ");
        result.Value.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task PublishNewsUseCase_WithDefaultChannel_EnsuresChannelAndPublishes()
    {
        var digest = new NewsDigestDto(
            "📰 IT Новости",
            "Вступление",
            [new NewsItemDto("Заголовок", "Суть", "Значение", null)],
            "Вопрос?");

        _gateway.EnsureNewsChannelAsync(_guildId, "новости", "📰 ИНФО И НОВОСТИ", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(_newsChannelId));

        _newsAi.GenerateNewsDigestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(digest));

        _gateway.PublishNewsDigestAsync(_guildId, _newsChannelId, digest, true, null, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new NewsPublicationResult(_newsChannelId, 999, 888)));

        var command = new PublishNews(_guildId);
        var result = await PublishNewsUseCase.Handle(command, _newsAi, _gateway, _guilds, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ChannelId.ShouldBe(_newsChannelId);
        result.Value.MessageId.ShouldBe(999UL);
        result.Value.ThreadId.ShouldBe(888UL);

        await _gateway.Received(1).EnsureNewsChannelAsync(_guildId, "новости", "📰 ИНФО И НОВОСТИ", Arg.Any<CancellationToken>());
        await _gateway.Received(1).PublishNewsDigestAsync(_guildId, _newsChannelId, digest, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetupNewsChannelUseCase_CallsEnsureNewsChannel()
    {
        _gateway.EnsureNewsChannelAsync(_guildId, "новости-дня", "📰 ДАЙДЖЕСТ", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(_newsChannelId));

        var command = new SetupNewsChannel(_guildId, "новости-дня", "📰 ДАЙДЖЕСТ");
        var result = await SetupNewsChannelUseCase.Handle(command, _gateway, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(_newsChannelId);
    }
}
