using System.Globalization;
using System.Text;
using MattGPT.Contracts.Models;

namespace MattGPT.ApiService.Services;

/// <summary>Renders a conversation's record as a downloadable Markdown file.</summary>
public static class ConversationRecordExport
{
    /// <summary>Longest file name stem; long titles are cut so the name stays usable on every file system.</summary>
    internal const int MaxFileNameStemChars = 100;

    private static readonly HashSet<char> InvalidFileNameChars =
        [.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>The Markdown document: the title as a heading, the conversation date, then the record.</summary>
    public static string BuildMarkdown(StoredConversation conversation)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(string.IsNullOrWhiteSpace(conversation.Title) ? "Untitled conversation" : conversation.Title.Trim());
        sb.AppendLine();

        if (conversation.CreateTime is { } created)
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds((long)(created * 1000)).UtcDateTime;
            sb.Append('*').Append(date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)).AppendLine("*");
            sb.AppendLine();
        }

        sb.AppendLine(conversation.Record?.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// A safe <c>.md</c> file name for <paramref name="title"/>: characters that file systems reject
    /// (<c>/ \ : * ? " &lt; &gt; |</c> and control characters) become dashes, runs of whitespace collapse,
    /// and leading/trailing dots and spaces are trimmed (Windows rejects them).
    /// </summary>
    public static string FileName(string? title)
    {
        var sb = new StringBuilder();
        foreach (var c in title ?? string.Empty)
        {
            if (InvalidFileNameChars.Contains(c) || char.IsControl(c))
                sb.Append('-');
            else if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
            }
            else
                sb.Append(c);
        }

        var stem = sb.ToString().Trim(' ', '.', '-');
        if (stem.Length > MaxFileNameStemChars)
            stem = stem[..MaxFileNameStemChars].TrimEnd(' ', '.', '-');

        return (stem.Length > 0 ? stem : "conversation") + ".md";
    }
}
