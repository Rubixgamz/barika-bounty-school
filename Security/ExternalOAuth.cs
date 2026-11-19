using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace School_Project.Security;

public static class ExternalOAuth
{
    public const string CookieScheme = "ExternalCookie";

    public static AuthenticationBuilder AddExternalProviders(
        this AuthenticationBuilder authentication,
        IConfiguration configuration)
    {
        authentication.AddCookie(CookieScheme, options =>
        {
            options.Cookie.Name = ".Barika.External";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
        });

        if (GetConfiguredScheme("google", configuration) is not null)
        {
            authentication.AddOAuth("Google", options => ConfigureProvider(
                options,
                configuration,
                "GOOGLE_CLIENT_ID",
                "GOOGLE_CLIENT_SECRET",
                "Authentication:Google",
                "/signin-google",
                "https://accounts.google.com/o/oauth2/v2/auth",
                "https://oauth2.googleapis.com/token",
                "https://openidconnect.googleapis.com/v1/userinfo",
                ["openid", "email", "profile"],
                "sub",
                "name",
                "email"));
        }

        if (GetConfiguredScheme("linkedin", configuration) is not null)
        {
            authentication.AddOAuth("LinkedIn", options => ConfigureProvider(
                options,
                configuration,
                "LINKEDIN_CLIENT_ID",
                "LINKEDIN_CLIENT_SECRET",
                "Authentication:LinkedIn",
                "/signin-linkedin",
                "https://www.linkedin.com/oauth/v2/authorization",
                "https://www.linkedin.com/oauth/v2/accessToken",
                "https://api.linkedin.com/v2/userinfo",
                ["openid", "profile", "email"],
                "sub",
                "name",
                "email"));
        }

        if (GetConfiguredScheme("github", configuration) is not null)
        {
            authentication.AddOAuth("GitHub", options =>
            {
                ConfigureProvider(
                    options,
                    configuration,
                    "GITHUB_CLIENT_ID",
                    "GITHUB_CLIENT_SECRET",
                    "Authentication:GitHub",
                    "/signin-github",
                    "https://github.com/login/oauth/authorize",
                    "https://github.com/login/oauth/access_token",
                    "https://api.github.com/user",
                    ["read:user", "user:email"],
                    "id",
                    "name",
                    "email");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "login");
                options.BackchannelHttpHandler = new GitHubUserAgentHandler();
                options.Events.OnCreatingTicket = AddVerifiedGitHubEmailAsync;
            });
        }

        return authentication;
    }

    public static string? GetConfiguredScheme(string provider, IConfiguration configuration)
    {
        var (clientIdName, clientSecretName, section) = provider.ToLowerInvariant() switch
        {
            "google" => ("GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET", "Authentication:Google"),
            "linkedin" => ("LINKEDIN_CLIENT_ID", "LINKEDIN_CLIENT_SECRET", "Authentication:LinkedIn"),
            "github" => ("GITHUB_CLIENT_ID", "GITHUB_CLIENT_SECRET", "Authentication:GitHub"),
            _ => (string.Empty, string.Empty, string.Empty)
        };

        if (section.Length == 0 ||
            string.IsNullOrWhiteSpace(ReadSetting(configuration, clientIdName, $"{section}:ClientId")) ||
            string.IsNullOrWhiteSpace(ReadSetting(configuration, clientSecretName, $"{section}:ClientSecret")))
            return null;

        return provider.ToLowerInvariant() switch
        {
            "google" => "Google",
            "linkedin" => "LinkedIn",
            "github" => "GitHub",
            _ => null
        };
    }

    private static void ConfigureProvider(
        OAuthOptions options,
        IConfiguration configuration,
        string clientIdName,
        string clientSecretName,
        string section,
        string callbackPath,
        string authorizationEndpoint,
        string tokenEndpoint,
        string userInformationEndpoint,
        string[] scopes,
        string subjectClaim,
        string nameClaim,
        string emailClaim)
    {
        options.ClientId = ReadSetting(configuration, clientIdName, $"{section}:ClientId") ?? string.Empty;
        options.ClientSecret = ReadSetting(configuration, clientSecretName, $"{section}:ClientSecret") ?? string.Empty;
        options.SignInScheme = CookieScheme;
        options.CallbackPath = callbackPath;
        options.AuthorizationEndpoint = authorizationEndpoint;
        options.TokenEndpoint = tokenEndpoint;
        options.UserInformationEndpoint = userInformationEndpoint;
        options.Scope.Clear();
        foreach (var scope in scopes) options.Scope.Add(scope);
        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, subjectClaim);
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, nameClaim);
        options.ClaimActions.MapJsonKey(ClaimTypes.Email, emailClaim);
        options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        options.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect("/register.html?error=provider");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    private static async Task AddVerifiedGitHubEmailAsync(OAuthCreatingTicketContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await context.Backchannel.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            context.HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: context.HttpContext.RequestAborted);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("primary", out var primary) && primary.GetBoolean() &&
                entry.TryGetProperty("verified", out var verified) && verified.GetBoolean() &&
                entry.TryGetProperty("email", out var email) && !string.IsNullOrWhiteSpace(email.GetString()))
            {
                var identity = context.Identity!;
                foreach (var existingEmail in identity.FindAll(ClaimTypes.Email).ToList())
                    identity.RemoveClaim(existingEmail);
                identity.AddClaim(new Claim(ClaimTypes.Email, email.GetString()!));
                identity.AddClaim(new Claim("email_verified", "true"));
                return;
            }
        }
    }

    private static string? ReadSetting(IConfiguration configuration, string environmentName, string configurationPath) =>
        Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationPath];

    private sealed class GitHubUserAgentHandler : DelegatingHandler
    {
        public GitHubUserAgentHandler() : base(new HttpClientHandler())
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.UserAgent.Any())
                request.Headers.UserAgent.ParseAdd("BarikaBounty/1.0");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
