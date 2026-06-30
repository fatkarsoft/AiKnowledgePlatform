using System.Text;
using AiKnowledgePlatform.Api.Features.Search;

namespace AiKnowledgePlatform.Api.AI;

public sealed class PromptBuilder
{
    private const string Instructions = """
You are a helpful assistant.
Answer only using the provided context.
If the answer is not in the context, say: 'Sağlanan bağlamda bu soruyu cevaplamak için yeterli bilgi yok.'
Answer in the same language as the user's question.
If the user asks in Turkish, answer in clear and natural Turkish.
Keep technical terms in English when the Turkish translation would sound unnatural or misleading.
Do not translate terms like cache, message broker, in-memory, replication, cluster, queue, exchange, routing key, dead letter queue, index, partitioning unless the context already uses a natural Turkish equivalent.
""";

    public string Build(string question, IReadOnlyList<SearchResult> searchResults)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(Instructions);
        prompt.AppendLine();
        prompt.AppendLine("Context:");

        foreach (var result in searchResults)
        {
            prompt.AppendLine($"--- Context Chunk {result.ChunkIndex} ---");
            prompt.AppendLine($"DocumentId: {result.DocumentId}");
            prompt.AppendLine($"FileName: {result.FileName}");
            prompt.AppendLine($"Score: {result.Score}");
            prompt.AppendLine(result.Text);
            prompt.AppendLine();
        }

        prompt.AppendLine("Question:");
        prompt.AppendLine(question);

        return prompt.ToString();
    }
}
