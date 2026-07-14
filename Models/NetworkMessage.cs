using System;
using System.Collections.Generic;

namespace P2PFil.Models
{
    public class NetworkMessage
    {
        public string Type { get; set; } = "FILE_LIST";

        // Kullanıcı adı
        public string Sender { get; set; } = "";

        // Kalıcı cihaz kimliği
        public string DeviceId { get; set; } = "";

        public List<SharedFile> Files { get; set; } = new();
    }
}