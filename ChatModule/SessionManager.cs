using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace P2PFil.ChatModule
{
    public class SessionInfo
    {
        public byte[] Key { get; set; } = Array.Empty<byte>();
        public DateTime LastActivity { get; set; }
        public string Fingerprint { get; set; } = string.Empty;

        // RAM'i korumak için HashSet yerine Queue tabanlı sınırlı kapasite
        private readonly Queue<string> _recentMessageIds = new();
        private const int MaxReplayCache = 1000;

        public bool CheckAndAddMessageId(string messageId)
        {
            lock (_recentMessageIds)
            {
                if (_recentMessageIds.Contains(messageId)) return true; // Replay saldırısı

                if (_recentMessageIds.Count >= MaxReplayCache)
                {
                    _recentMessageIds.Dequeue(); // En eskiyi sil
                }
                _recentMessageIds.Enqueue(messageId);
                return false;
            }
        }
    }

    public sealed class SessionManager : IDisposable
    {
        private static readonly Lazy<SessionManager> _instance = new(() => new SessionManager());
        public static SessionManager Instance => _instance.Value;
        private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

        public void CreateOrUpdateSession(IPAddress remoteIp, byte[] aesKey, string fingerprint)
        {
            var session = new SessionInfo
            {
                Key = aesKey,
                LastActivity = DateTime.Now,
                Fingerprint = fingerprint
            };
            _sessions[remoteIp.ToString()] = session;
        }

        public bool IsMessageProcessed(IPAddress remoteIp, string messageId)
        {
            if (_sessions.TryGetValue(remoteIp.ToString(), out var session))
            {
                return session.CheckAndAddMessageId(messageId);
            }
            return false;
        }

        public void CleanupExpiredSessions()
        {
            var now = DateTime.Now;
            foreach (var kvp in _sessions)
            {
                if (now - kvp.Value.LastActivity > TimeSpan.FromMinutes(30))
                {
                    RemoveSession(IPAddress.Parse(kvp.Key));
                }
            }
        }

        public bool TryGetSessionKey(IPAddress remoteIp, out byte[] aesKey)
        {
            if (remoteIp != null && _sessions.TryGetValue(remoteIp.ToString(), out var session))
            {
                session.LastActivity = DateTime.Now;
                aesKey = session.Key;
                return true;
            }
            aesKey = Array.Empty<byte>();
            return false;
        }

        public bool HasSession(IPAddress remoteIp)
        {
            if (remoteIp == null) return false;
            return _sessions.ContainsKey(remoteIp.ToString());
        }

        public void RemoveSession(IPAddress remoteIp)
        {
            if (remoteIp == null) return;

            if (_sessions.TryRemove(remoteIp.ToString(), out var session))
            {
                Array.Clear(session.Key, 0, session.Key.Length);
            }
        }

        public void ClearAll()
        {
            foreach (var kvp in _sessions)
            {
                Array.Clear(kvp.Value.Key, 0, kvp.Value.Key.Length);
            }
            _sessions.Clear();
        }

        public void Dispose() => ClearAll();
    }
}