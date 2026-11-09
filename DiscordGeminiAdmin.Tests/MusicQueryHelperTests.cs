using DiscordGeminiAdmin.Presentation.Discord.Components;
using Xunit;

namespace DiscordGeminiAdmin.Tests;

public sealed class MusicQueryHelperTests
{
    [Theory]
    [InlineData("query: eminem", "eminem")]
    [InlineData("query:   Linkin Park Numb  ", "Linkin Park Numb")]
    [InlineData("\"МБ Пакет - Господин президент\"", "МБ Пакет - Господин президент")]
    [InlineData("query: \"МБ Пакет - Господин президент\"", "МБ Пакет - Господин президент")]
    [InlineData("'https://youtu.be/12345'", "https://youtu.be/12345")]
    [InlineData("https://www.youtube.com/watch?v=123", "https://www.youtube.com/watch?v=123")]
    [InlineData("ytsearch:test query", "ytsearch:test query")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Clean_ShouldProperlyNormalizeQueries(string input, string expected)
    {
        var result = MusicQueryHelper.Clean(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://youtu.be/123", true)]
    [InlineData("http://youtube.com/watch?v=123", true)]
    [InlineData("eminem", false)]
    public void IsUrl_ShouldIdentifyHttpUrls(string input, bool expected)
    {
        Assert.Equal(expected, MusicQueryHelper.IsUrl(input));
    }

    [Theory]
    [InlineData("ytsearch:eminem", true)]
    [InlineData("scsearch:eminem", true)]
    [InlineData("ymsearch:eminem", true)]
    [InlineData("eminem", false)]
    public void HasSearchPrefix_ShouldIdentifySearchPrefixes(string input, bool expected)
    {
        Assert.Equal(expected, MusicQueryHelper.HasSearchPrefix(input));
    }
}
