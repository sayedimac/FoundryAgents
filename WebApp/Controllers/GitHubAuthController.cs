using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WebApp.Services;

namespace WebApp.Controllers;

[Route("auth/github")]
public class GitHubAuthController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IGitHubTokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubAuthController> _logger;

    public GitHubAuthController(
        IConfiguration configuration,
        IGitHubTokenService tokenService,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubAuthController> logger)
    {
        _configuration = configuration;
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var sessionId = GetOrCreateSessionId();
        var isAuthenticated = _tokenService.IsAuthenticated(sessionId);
        var username = isAuthenticated ? _tokenService.GetUsername(sessionId) : null;

        return Ok(new { authenticated = isAuthenticated, username });
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        var clientId = _configuration["GitHub:AppClientId"];
        if (string.IsNullOrEmpty(clientId))
        {
            return BadRequest("GitHub App is not configured. Set GitHub:AppClientId in configuration.");
        }

        // Generate and store anti-forgery state
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString("github_oauth_state", state);
        HttpContext.Session.SetString("github_return_url", returnUrl ?? "/");

        var callbackUrl = Url.Action("Callback", "GitHubAuth", null, Request.Scheme);
        // Scopes required by the GitHub MCP server (api.githubcopilot.com/mcp)
        var scope = "repo read:org user user:email";
        var authorizeUrl = $"https://github.com/login/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUrl!)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&state={state}";

        return Redirect(authorizeUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
    {
        // Verify anti-forgery state
        var expectedState = HttpContext.Session.GetString("github_oauth_state");
        if (string.IsNullOrEmpty(expectedState) || state != expectedState)
        {
            _logger.LogWarning("GitHub OAuth state mismatch");
            return BadRequest("Invalid state parameter. Please try logging in again.");
        }

        HttpContext.Session.Remove("github_oauth_state");

        var clientId = _configuration["GitHub:AppClientId"];
        var clientSecret = _configuration["GitHub:AppClientSecret"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return BadRequest("GitHub App is not configured.");
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();

            // Exchange authorization code for user access token
            // https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app
            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code
            });

            var tokenResponse = await httpClient.SendAsync(tokenRequest);
            tokenResponse.EnsureSuccessStatusCode();

            var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();

            if (tokenJson.TryGetProperty("error", out var error))
            {
                var errorDesc = tokenJson.TryGetProperty("error_description", out var desc)
                    ? desc.GetString()
                    : "Unknown error";
                _logger.LogError("GitHub OAuth error: {Error} - {Description}", error.GetString(), errorDesc);
                return BadRequest($"GitHub authentication failed: {errorDesc}");
            }

            var accessToken = tokenJson.GetProperty("access_token").GetString()!;

            // Fetch the authenticated GitHub user's profile
            string? username = null;
            try
            {
                var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                userRequest.Headers.UserAgent.ParseAdd("GitHubAgent-WebApp/1.0");

                var userResponse = await httpClient.SendAsync(userRequest);
                if (userResponse.IsSuccessStatusCode)
                {
                    var userJson = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
                    username = userJson.GetProperty("login").GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch GitHub user profile");
            }

            // Store the access token keyed to this user's session
            var sessionId = GetOrCreateSessionId();
            _tokenService.StoreToken(sessionId, accessToken, username);

            _logger.LogInformation("GitHub user authenticated: {Username}", username);

            // Redirect back to the chat page with the GitHub agent pre-selected
            var returnUrl = HttpContext.Session.GetString("github_return_url") ?? "/";
            HttpContext.Session.Remove("github_return_url");

            return Redirect($"{returnUrl}?agent=GitHub&github_auth=success" +
                $"&username={Uri.EscapeDataString(username ?? "")}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub OAuth token exchange failed");
            return BadRequest("Failed to authenticate with GitHub. Please try again.");
        }
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var sessionId = GetOrCreateSessionId();
        _tokenService.RemoveToken(sessionId);
        return Ok(new { success = true });
    }

    private string GetOrCreateSessionId()
    {
        var sessionId = HttpContext.Session.GetString("app_session_id");
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = Guid.NewGuid().ToString();
            HttpContext.Session.SetString("app_session_id", sessionId);
        }
        return sessionId;
    }
}
