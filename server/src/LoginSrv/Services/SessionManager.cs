namespace LoginSrv.Services
{
    public class SessionManager
    {
        private readonly object gate = new();
        private readonly Dictionary<int, SessionConnInfo> sessionMap = new();
        private readonly Dictionary<string, SessionConnInfo> sessionAccountMap = new();

        public void AddSession(int sessionId, SessionConnInfo sessionConnInfo)
        {
            lock (gate)
            {
                sessionMap.Add(sessionId, sessionConnInfo);
                sessionAccountMap.Add(sessionConnInfo.Account, sessionConnInfo);
            }
        }

        public SessionConnInfo GetSession(string account)
        {
            lock (gate) return sessionAccountMap.TryGetValue(account, out var session) ? session : null;
        }

        public void UpdateSession(int sessionId, string serverName, bool isPayMent)
        {
            lock (gate) if (sessionMap.TryGetValue(sessionId, out var session))
            {
                session.ServerName = serverName;
                session.IsPayMent = isPayMent;
            }
        }

        public bool IsLogin(int sessionId) { lock (gate) return sessionMap.ContainsKey(sessionId); }
        public bool IsLogin(string account) { lock (gate) return sessionAccountMap.ContainsKey(account); }

        public void Delete(string account, int sessionId)
        {
            lock (gate)
            {
                if (!sessionMap.TryGetValue(sessionId, out var session) || session.Account != account) return;
                sessionMap.Remove(sessionId);
                if (sessionAccountMap.TryGetValue(account, out var active) && active.SessionID == sessionId)
                    sessionAccountMap.Remove(account);
            }
        }

        public bool TryReleaseSession(string account, int sessionId, string serverName)
        {
            lock (gate)
            {
                if (!sessionMap.TryGetValue(sessionId, out var session) || session.Account != account
                    || !string.Equals(session.ServerName, serverName, StringComparison.OrdinalIgnoreCase)) return false;
                sessionMap.Remove(sessionId);
                if (sessionAccountMap.TryGetValue(account, out var active) && active.SessionID == sessionId)
                    sessionAccountMap.Remove(account);
                return true;
            }
        }

        public SessionConnInfo[] GetSessions()
        {
            lock (gate) return sessionMap.Count > 0 ? sessionMap.Values.ToArray() : null;
        }
    }
}
