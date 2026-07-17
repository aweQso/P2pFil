# P2PFil

A peer-to-peer file sharing app built with .NET MAUI. Discover devices on your local network and share files directly, no server required.

V12.14.17

## Features

- Local network peer discovery (UDP broadcast)
- Encrypted file transfer (AES + key exchange over TCP)
- Resumable downloads — pick up where you left off after a disconnect
- Concurrent transfer queue (limits active downloads, queues the rest)
- Persistent download progress across app restarts

## Recent Updates

- **UI improvements**
  - Refreshed Files page layout and styling
  - Profil page add and profile picture feature and saveable name
  - Progress panel now stays visible during auto-reconnect attempts

- **Reliability fixes**
  - Fixed duplicate file cards showing up after a peer reconnects with a new IP (now tracked by persistent Device ID instead of IP)
  - Added automatic reconnect: if the connection drops on the sender's side, the app retries silently in the background (fixed 2s interval, unlimited retries) instead of requiring the user to cancel and restart
  - Added inactivity watchdog to detect silently dead connections (e.g. sender loses internet) that previously hung forever with no error
  - "Resume" now only appears when the disconnect is local (app closed, own internet lost) — not during automatic background reconnection

- **Performance optimizations**
  - Removed redundant re-hashing of already-downloaded bytes on every resume/retry — full file integrity check now runs only once, when the download actually completes
  - Increased transfer buffer size (128KB) to reduce per-chunk overhead on fast connections, balanced against mobile memory constraints

## Tech Stack

- .NET MAUI
- TCP/UDP sockets
- AES encryption for file transfers
- JSON-based local persistence (no external DB)
