using System;

namespace ChatShared
{
    public class ChatMessage
    {
        public string From { get; set; }
        public string Message { get; set; }
        public string Type { get; set; }   // "join", "chat", "system"

        // Otomatis isi waktu ketika objek dibuat
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
