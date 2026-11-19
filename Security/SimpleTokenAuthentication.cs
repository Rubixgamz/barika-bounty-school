using System.Security.Claims;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using School_Project.Data;
using School_Project.Models;

namespace School_Project.Security;

public static class SimpleTokenAuthentication
{
    public const string Scheme = "SchoolProjectToken";
}

public sealed class AuthSessionService(IAppRepository repository)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, (User User, DateTimeOffset ExpiresAt)> _sessions = new(StringComparer.Ordinal);
    private readonly PasswordHasher<User> _passwordHasher = new();

    public async Task<LoginResponse?> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return null;

        var user = await repository.FindUserByEmailAsync(email);
        if (user is null || !user.IsActive ||
            _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed) return null;

        return CreateSession(user);
    }

    public LoginResponse CreateSession(User user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = (user, DateTimeOffset.UtcNow.Add(SessionLifetime));
        return new LoginResponse(token, ToProfile(user));
    }

    public bool TryGetUser(string token, out User user)
    {
        user = null!;
        if (!_sessions.TryGetValue(token, out var session)) return false;
        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        user = session.User;
        return true;
    }

    public void Revoke(string token) => _sessions.TryRemove(token, out _);

    public void RevokeUserSessions(int userId)
    {
        foreach (var session in _sessions)
        {
            if (session.Value.User.Id == userId) _sessions.TryRemove(session.Key, out _);
        }
    }

    public static UserProfile ToProfile(User user) =>
        new(user.Id, user.FullName, user.Email, user.Role, user.OrganizationId, user.IsActive);
}

public sealed class SimpleTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthSessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";

        if (!authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authHeader[prefix.Length..].Trim();
        if (!sessions.TryGetUser(token, out var user))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid token."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("organization_id", user.OrganizationId?.ToString() ?? string.Empty)
        };
        var identity = new ClaimsIdentity(claims, SimpleTokenAuthentication.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SimpleTokenAuthentication.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
