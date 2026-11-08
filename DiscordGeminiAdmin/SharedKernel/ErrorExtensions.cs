using FluentResults;

namespace DiscordGeminiAdmin.SharedKernel;

public static class ErrorExtensions
{
    extension(Error error)
    {
        public Error WithErrorCode(string errorCode) => error.WithMetadata("ErrorCode", errorCode);
    }
}
