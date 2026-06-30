using System.Text.Json.Serialization;

namespace AiKnowledgePlatform.Api.Features.Documents;

[JsonConverter(typeof(JsonStringEnumConverter<DocumentStatus>))]
public enum DocumentStatus
{
    Uploaded,
    Processing,
    Chunked,
    Embedded,
    Indexed,
    Ready,
    Failed
}
