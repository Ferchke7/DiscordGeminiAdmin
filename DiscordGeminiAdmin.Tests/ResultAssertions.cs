using FluentResults;
using Shouldly;

namespace DiscordGeminiAdmin.Tests;

public static class ResultAssertions
{
    extension(ResultBase result)
    {
        /// <summary>Проверяет, что Result провалился с ошибкой, у которой метаданные ErrorCode равны <paramref name="code"/>.</summary>
        public void ShouldHaveErrorCode(string code)
        {
            result.IsFailed.ShouldBeTrue();
            result.Errors.ShouldContain(e => (string?)e.Metadata.GetValueOrDefault("ErrorCode") == code);
        }
    }
}
