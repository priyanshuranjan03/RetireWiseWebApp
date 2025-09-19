namespace RetireWiseWebApp.Models
{
    public class AgentMessage
    {
        public MessageType Type { get; set; }
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public enum MessageType
    {
        System,
        User,
        Assistant
    }
}