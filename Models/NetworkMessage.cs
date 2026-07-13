namespace P2PFil.Models;

public class NetworkMessage
{
    public string Type { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty; // Kullanıcı ismi
    public List<SharedFile> Files { get; set; } = new();
}