# P2PFil - Secure Peer-to-Peer File Sharing & Chat Application

P2PFil is a high-performance, fully encrypted, and decentralized peer-to-peer (P2P) file sharing and instant messaging application designed for Local Area Networks (LAN).

The application is built with industry-standard end-to-end encryption (E2EE), dynamic network discovery (UDP/TCP), and optimized disk I/O management.

---  V11.14.14

## 🚀 Key Features & Recent Updates

The latest architectural update has redesigned the system's security infrastructure, network engine, and memory management to meet professional industry standards.

### 🔐 Advanced Security and Cryptography
*   **Forward Secrecy:** Static identity keys are no longer used for direct encryption; they are used only for signing to verify identity[cite: 22, 23]. Ephemeral Diffie-Hellman keys are generated via the `KeyExchangeService` for every session, ensuring the security of past messages[cite: 23].
*   **Secure Identity Store (Atomic Writes):** Fingerprint records in the `PeerTrustStore` are now written atomically[cite: 21]. This prevents data corruption during crashes and ensures full protection against Man-in-the-Middle (MitM) attacks[cite: 21].
*   **Single-Layer Media Encryption:** The double encryption bug in large file transfers has been resolved[cite: 20]. Packet sizes have been optimized by 33%, and performance bottlenecks have been eliminated[cite: 20].

### ⚡ Performance & Network Optimization (`NetworkService`)
*   **Zero Memory Leak:** Background UDP and TCP listener tasks are now managed using `CancellationTokenSource` and `IDisposable` patterns. This prevents "zombie" threads from occupying RAM when the service restarts.
*   **Intelligent Disk I/O Caching:** The file list broadcasted to peers now utilizes a 30-second intelligent cache (`cachedMessageBytes`) instead of polling the disk every 5 seconds. This eliminates disk bottlenecks and latency spikes.
*   **Expanded Network Throughput:** The read buffer for data transfers has been increased from 8 KB to **64 KB**. The AES-GCM encryption cycle has been optimized to fully utilize local network capacity.

---

## 🛠️ Technical Architecture

| Component | Technology / Protocol | Responsibility |
| :--- | :--- | :--- |
| **Discovery** | UDP Broadcast (Port 8888) | Automatic peer discovery and file list synchronization[cite: 24]. |
| **Transfer** | TCP Sockets (Port 8889) | Secure handshake, command processing, and high-speed data transfer[cite: 24]. |
| **Encryption** | AES-GCM & ECDH | End-to-end encrypted messaging and file integrity verification[cite: 20, 23]. |
| **UI** | .NET MAUI | Cross-platform compatibility and asynchronous UI updates[cite: 18]. |

---

## 💻 Installation & Usage

### Prerequisites
*   .NET 8.0 SDK or later
*   Supported IDE (Visual Studio 2022 / JetBrains Rider)
*   Andorid and Windows
