using System.Text.Json.Serialization;

namespace RetireWiseWebApp.Models;

public class ChatRequest
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("threadId")]
    public string? ThreadId { get; set; }
}

public class ChatResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;
    
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class UploadKnowledgeRequest
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;
    
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }
}
