using System;
using System.Collections.Generic;

namespace P2PFil.Models
{
    public class NetworkMessage
    {
        public string Type { get; set; } = "FILE_LIST"; // Varsayılan tip mesajı[cite: 16]
        public string Sender { get; set; } = string.Empty; //[cite: 16]
        public List<SharedFile> Files { get; set; } = new List<SharedFile>(); //[cite: 16]
    }
}