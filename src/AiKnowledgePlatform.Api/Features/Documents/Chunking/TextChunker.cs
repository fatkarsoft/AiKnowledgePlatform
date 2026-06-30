using System.Text;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Api.Features.Documents.Chunking;

public sealed class TextChunker
{
    private readonly int _targetChunkSize;
    private readonly int _overlapSize;

    public TextChunker(IOptions<ChunkingOptions> options)
    {
        _targetChunkSize = options.Value.TargetChunkSize;
        _overlapSize = options.Value.OverlapSize;
    }

    public IReadOnlyList<DocumentChunk> CreateChunks(Guid documentId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = new List<DocumentChunk>();
        foreach (var textChunk in SplitText(text))
        {
            if (string.IsNullOrWhiteSpace(textChunk))
            {
                continue;
            }

            chunks.Add(new DocumentChunk(
                documentId,
                chunks.Count,
                textChunk,
                textChunk.Length,
                DateTime.UtcNow));
        }

        return chunks;
    }

    private IEnumerable<string> SplitText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var buffer = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            foreach (var unit in SplitParagraph(paragraph))
            {
                if (buffer.Length > 0 && WouldExceedTarget(buffer, unit))
                {
                    var chunk = buffer.ToString().Trim();
                    yield return chunk;

                    buffer.Clear();

                    // Overlap exists to carry meaning forward. Complete trailing sentences are
                    // more useful than an arbitrary character slice, even when they are not
                    // exactly 150 characters.
                    var overlap = GetSemanticOverlap(chunk);
                    if (!string.IsNullOrWhiteSpace(overlap))
                    {
                        buffer.Append(overlap);
                    }
                }

                AppendUnit(buffer, unit);
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().Trim();
        }
    }

    private IEnumerable<string> SplitParagraph(string paragraph)
    {
        var trimmed = paragraph.Trim();
        if (trimmed.Length <= _targetChunkSize)
        {
            yield return trimmed;
            yield break;
        }

        foreach (var sentence in SplitIntoSentences(trimmed))
        {
            if (sentence.Length <= _targetChunkSize)
            {
                yield return sentence;
                continue;
            }

            // If a single sentence or sentence-less paragraph is too large, word-safe
            // splitting is the least surprising fallback without introducing NLP tooling.
            foreach (var splitSentence in SplitLongText(sentence))
            {
                yield return splitSentence;
            }
        }
    }

    private IEnumerable<string> SplitLongText(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            var length = Math.Min(_targetChunkSize, remaining);

            if (length < remaining)
            {
                var lastSpace = text.LastIndexOf(' ', start + length, length);
                if (lastSpace > start)
                {
                    length = lastSpace - start;
                }
            }

            var chunk = text.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return chunk;
            }

            start += Math.Max(length - _overlapSize, 1);
        }
    }

    private static IReadOnlyList<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (!IsSentenceTerminator(text[i]))
            {
                continue;
            }

            if (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))
            {
                continue;
            }

            var sentence = text[start..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                sentences.Add(sentence);
            }

            start = i + 1;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        var remainder = text[start..].Trim();
        if (!string.IsNullOrWhiteSpace(remainder))
        {
            sentences.Add(remainder);
        }

        return sentences.Count > 0 ? sentences : [text.Trim()];
    }

    private string GetSemanticOverlap(string text)
    {
        var completeSentences = GetCompleteSentences(text);
        if (completeSentences.Count == 0)
        {
            return GetCharacterOverlap(text);
        }

        var selected = new Stack<string>();
        var length = 0;

        for (var i = completeSentences.Count - 1; i >= 0; i--)
        {
            var sentence = completeSentences[i];
            var separatorLength = selected.Count == 0 ? 0 : 1;

            if (selected.Count > 0 && length + separatorLength + sentence.Length > _overlapSize)
            {
                break;
            }

            selected.Push(sentence);
            length += separatorLength + sentence.Length;

            if (length >= _overlapSize)
            {
                break;
            }
        }

        // A single complete sentence that is longer than the nominal overlap still carries
        // cleaner context than starting halfway through that sentence.
        if (selected.Count == 0)
        {
            selected.Push(completeSentences[^1]);
        }

        return string.Join(' ', selected);
    }

    private static IReadOnlyList<string> GetCompleteSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (!IsSentenceTerminator(text[i]))
            {
                continue;
            }

            if (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))
            {
                continue;
            }

            var sentence = text[start..(i + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                sentences.Add(sentence);
            }

            start = i + 1;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        return sentences;
    }

    private string GetCharacterOverlap(string text)
    {
        if (text.Length <= _overlapSize)
        {
            return text;
        }

        var start = text.Length - _overlapSize;
        var firstSpace = text.IndexOf(' ', start);

        return firstSpace >= 0
            ? text[firstSpace..].Trim()
            : text[start..].Trim();
    }

    private bool WouldExceedTarget(StringBuilder buffer, string unit)
    {
        var separatorLength = buffer.Length == 0 ? 0 : Environment.NewLine.Length * 2;
        return buffer.Length + separatorLength + unit.Length > _targetChunkSize;
    }

    private static void AppendUnit(StringBuilder buffer, string unit)
    {
        if (buffer.Length > 0)
        {
            buffer.AppendLine();
            buffer.AppendLine();
        }

        buffer.Append(unit.Trim());
    }

    private static bool IsSentenceTerminator(char character)
    {
        return character is '.' or '!' or '?';
    }
}
