namespace WebApp.Services;

public interface IGitHubTokenService
{
    void StoreToken(string sessionId, string accessToken, string? username);
    string? GetToken(string sessionId);
    string? GetUsername(string sessionId);
    bool IsAuthenticated(string sessionId);
    void RemoveToken(string sessionId);
}
