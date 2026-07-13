using System;

namespace P2PFil.ChatModule
{
    public class ChatMessage
    {
        public string SenderName { get; set; } = string.Empty;
        public string SenderIp { get; set; } = string.Empty;

        // EKSİK OLAN KISIM BURASI:
        public string TargetName { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        // Mesajın kimin olduğunu belirlemek için
        public bool IsMe { get; set; }

        public string MessageType { get; set; } = "Text";
        public string EncryptedBase64Media { get; set; } = string.Empty;
        public string LocalMediaPath { get; set; } = string.Empty;
    }
}