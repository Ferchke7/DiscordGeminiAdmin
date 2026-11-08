using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.IngestMessage;

public sealed record IngestMessage(
    GuildId GuildId,
    ChannelId ChannelId,
    MemberId AuthorId,
    MessageId DiscordMessageId,
    string AuthorName,
    string Content);

public static class IngestMessageUseCase
{
    public static async ValueTask<Result> Handle(
        IngestMessage command,
        IUnitOfWork unitOfWork,
        IBufferedMessageRepository messages,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Content))
            return Result.Ok();

        var buffered = BufferedMessage.Create(
            command.GuildId,
            command.ChannelId,
            command.AuthorId,
            command.DiscordMessageId,
            command.AuthorName,
            command.Content,
            now);

        messages.Add(buffered);
        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
