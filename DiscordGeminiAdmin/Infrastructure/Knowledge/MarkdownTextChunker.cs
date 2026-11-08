using System.Text;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;

namespace DiscordGeminiAdmin.Infrastructure.Knowledge;

/// <summary>Разбивает документ базы знаний по заголовкам (# / ##) и длине (~1200 символов) с сохранением идентификатора правила.</summary>
internal sealed class MarkdownTextChunker : ITextChunker
{
    private const int MaxChunkLength = 1200;

    public IReadOnlyList<TextChunkDto> Chunk(string baseRuleCode, string title, string content)
    {
        var normalized = content.Replace("\r\n", "\n").Trim();
        if (normalized.Length <= MaxChunkLength)
            return [new TextChunkDto(baseRuleCode, title, normalized)];

        var lines = normalized.Split('\n');
        var chunks = new List<TextChunkDto>();
        var current = new StringBuilder();
        var currentTitle = title;
        var index = 1;

        void Flush()
        {
            var text = current.ToString().Trim();
            if (text.Length == 0)
                return;

            var code = index == 1 && chunks.Count == 0 ? baseRuleCode : $"{baseRuleCode}.{index}";
            chunks.Add(new TextChunkDto(code, currentTitle, text));
            current.Clear();
            index++;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var isHeader = line.StartsWith("# ", StringComparison.Ordinal)
                           || line.StartsWith("## ", StringComparison.Ordinal)
                           || line.StartsWith("### ", StringComparison.Ordinal);

            if (isHeader && current.Length > 0)
            {
                Flush();
                currentTitle = line.TrimStart('#', ' ').Trim();
            }

            if (current.Length + line.Length + 1 > MaxChunkLength && current.Length > 0)
            {
                Flush();
            }

            current.AppendLine(line);
        }

        Flush();
        return chunks;
    }
}
