using System.Collections.Concurrent;

namespace WebApp.Services;

public class GitHubTokenService : IGitHubTokenService
{
    private readonly ConcurrentDictionary<string, (string AccessToken, string? Username)> _tokens = new();

    public void StoreToken(string sessionId, string accessToken, string? username)
    {
        _tokens[sessionId] = (accessToken, username);
    }

    public string? GetToken(string sessionId)
    {
        return _tokens.TryGetValue(sessionId, out var info) ? info.AccessToken : null;
    }

    public string? GetUsername(string sessionId)
    {
        return _tokens.TryGetValue(sessionId, out var info) ? info.Username : null;
    }

    public bool IsAuthenticated(string sessionId)
    {
        return _tokens.ContainsKey(sessionId);
    }

    public void RemoveToken(string sessionId)
    {
        _tokens.TryRemove(sessionId, out _);
    }
}
