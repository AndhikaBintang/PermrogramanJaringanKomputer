using System;

namespace ChatShared
{
    public class ChatMessage
    {
        public string type { get; set; }   // "join", "msg", "pm", "sys", "users"
        public string from { get; set; }
        public string to { get; set; }
        public string text { get; set; }
        public long ts { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
