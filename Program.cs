using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json.Serialization;
using School_Project.Application;
using School_Project.Data;
using School_Project.Models;
using School_Project.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net.Mail;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_URLS")))
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<IAppRepository, InMemoryAppRepository>();
builder.Services.AddSingleton<AuthSessionService>();
builder.Services.AddScoped<ChallengeService>();
builder.Services.AddScoped<ProposalService>();
builder.Services.AddScoped<EvaluationService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddAuthentication(SimpleTokenAuthentication.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SimpleTokenAuthenticationHandler>(
        SimpleTokenAuthentication.Scheme,
        _ => { })
    .AddExternalProviders(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdministratorOnly", policy => policy.RequireRole(UserRole.Admin.ToString()));
    options.AddPolicy("EvaluatorOnly", policy => policy.RequireRole(UserRole.Evaluator.ToString(), UserRole.Admin.ToString()));
    options.AddPolicy("ChallengePublisher", policy => policy.RequireRole(UserRole.Admin.ToString(), UserRole.OrganizationOwner.ToString()));
    options.AddPolicy("InnovatorOnly", policy => policy.RequireRole(UserRole.Innovator.ToString()));
    options.AddPolicy("DashboardViewer", policy => policy.RequireRole(UserRole.Admin.ToString(), UserRole.OrganizationOwner.ToString()));
});
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/auth/login", async (LoginRequest request, AuthSessionService auth) =>
{
    var result = await auth.LoginAsync(request.Email, request.Password);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

app.MapGet("/api/auth/providers", (IConfiguration configuration) => Results.Ok(new
{
    google = ExternalOAuth.GetConfiguredScheme("google", configuration) is not null,
    linkedIn = ExternalOAuth.GetConfiguredScheme("linkedin", configuration) is not null,
    github = ExternalOAuth.GetConfiguredScheme("github", configuration) is not null
}));

app.MapPost("/api/auth/register", async (RegisterRequest request, IAppRepository repository, AuthSessionService auth) =>
{
    var fullName = request.FullName?.Trim();
    var email = request.Email?.Trim();
    if (string.IsNullOrWhiteSpace(fullName) ||
        !MailAddress.TryCreate(email, out var mailAddress) ||
        !string.Equals(mailAddress.Address, email, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Enter a valid name and email address.");
    if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
        return Results.BadRequest("Choose a password with at least 8 characters.");
    if (request.Role is not (UserRole.Innovator or UserRole.OrganizationOwner))
        return Results.BadRequest("Choose an individual or organization account.");

    if (request.Role == UserRole.OrganizationOwner)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationName) ||
            string.IsNullOrWhiteSpace(request.Sector) ||
            string.IsNullOrWhiteSpace(request.Location))
            return Results.BadRequest("Organization name, sector, and location are required.");

        var registration = await repository.RegisterOrganizationOwnerAsync(
            new User(0, fullName, email, string.Empty, UserRole.OrganizationOwner, null),
            request.Password,
            new Organization(0, request.OrganizationName.Trim(), request.Sector.Trim(), request.Location.Trim()),
            isActive: false);
        return registration is null
            ? Results.Conflict("An account already uses this email or organization name.")
            : Results.Accepted(value: new
            {
                requiresApproval = true,
                message = "Your organization registration is awaiting administrator approval."
            });
    }

    var user = await repository.AddUserAsync(
        new User(0, fullName, email, string.Empty, UserRole.Innovator, null),
        request.Password!);
    return user is null
        ? Results.Conflict("An account with this email already exists.")
        : Results.Ok(auth.CreateSession(user));
});

app.MapGet("/api/auth/external/{provider}", async (
    string provider,
    HttpContext context,
    IConfiguration configuration,
    string? mode,
    UserRole? role,
    string? fullName,
    string? organizationName,
    string? sector,
    string? location) =>
{
    var scheme = ExternalOAuth.GetConfiguredScheme(provider, configuration);
    if (scheme is null) return Results.BadRequest("This sign-in provider is not configured on the server.");
    if (mode is not ("signin" or "register"))
        return Results.BadRequest("Choose sign in or register.");
    if (mode == "register" &&
        (role is not (UserRole.Innovator or UserRole.OrganizationOwner) ||
         string.IsNullOrWhiteSpace(fullName) ||
         (role == UserRole.OrganizationOwner &&
          (string.IsNullOrWhiteSpace(organizationName) || string.IsNullOrWhiteSpace(sector) || string.IsNullOrWhiteSpace(location)))))
        return Results.BadRequest("Complete the required registration details.");

    await context.SignOutAsync(ExternalOAuth.CookieScheme);
    var properties = new AuthenticationProperties { RedirectUri = "/auth-complete.html" };
    properties.Items["mode"] = mode;
    if (role.HasValue) properties.Items["role"] = role.Value.ToString();
    if (!string.IsNullOrWhiteSpace(fullName)) properties.Items["fullName"] = fullName.Trim();
    if (!string.IsNullOrWhiteSpace(organizationName)) properties.Items["organizationName"] = organizationName.Trim();
    if (!string.IsNullOrWhiteSpace(sector)) properties.Items["sector"] = sector.Trim();
    if (!string.IsNullOrWhiteSpace(location)) properties.Items["location"] = location.Trim();
    return Results.Challenge(properties, [scheme]);
});

app.MapPost("/api/auth/external/complete", async (
    HttpContext context,
    IAppRepository repository,
    AuthSessionService auth) =>
{
    var external = await context.AuthenticateAsync(ExternalOAuth.CookieScheme);
    await context.SignOutAsync(ExternalOAuth.CookieScheme);
    if (!external.Succeeded || external.Principal is null || external.Properties is null)
        return Results.Unauthorized();

    var email = external.Principal.FindFirstValue(ClaimTypes.Email);
    var emailVerified = external.Principal.FindFirstValue("email_verified");
    if (string.IsNullOrWhiteSpace(email) || !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("The provider did not return a verified email address.");

    var mode = GetAuthItem(external.Properties.Items, "mode");
    var user = await repository.FindUserByEmailAsync(email);
    if (mode == "signin")
    {
        if (user is null) return Results.Unauthorized();
        if (!user.IsActive)
            return Results.Json(new { approvalPending = true, message = "Your account is awaiting administrator approval." }, statusCode: StatusCodes.Status403Forbidden);
        return Results.Ok(auth.CreateSession(user));
    }

    if (mode != "register") return Results.BadRequest("The sign-in request has expired. Please try again.");
    if (user is not null) return Results.Conflict("An account already uses this email. Please sign in instead.");

    var fullName = external.Principal.FindFirstValue(ClaimTypes.Name)
        ?? GetAuthItem(external.Properties.Items, "fullName")
        ?? email.Split('@')[0];
    if (!Enum.TryParse<UserRole>(GetAuthItem(external.Properties.Items, "role"), out var role) ||
        role is not (UserRole.Innovator or UserRole.OrganizationOwner))
        return Results.BadRequest("Choose an individual or organization account.");

    var generatedPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    if (role == UserRole.OrganizationOwner)
    {
        var organizationName = GetAuthItem(external.Properties.Items, "organizationName");
        var sector = GetAuthItem(external.Properties.Items, "sector");
        var location = GetAuthItem(external.Properties.Items, "location");
        if (string.IsNullOrWhiteSpace(organizationName) || string.IsNullOrWhiteSpace(sector) || string.IsNullOrWhiteSpace(location))
            return Results.BadRequest("Complete the organization registration details.");

        var registration = await repository.RegisterOrganizationOwnerAsync(
            new User(0, fullName, email, string.Empty, UserRole.OrganizationOwner, null),
            generatedPassword,
            new Organization(0, organizationName, sector, location),
            isActive: false);
        return registration is null
            ? Results.Conflict("An account already uses this email or organization name.")
            : Results.Accepted(value: new
            {
                requiresApproval = true,
                message = "Your organization registration is awaiting administrator approval."
            });
    }

    user = await repository.AddUserAsync(
        new User(0, fullName, email, string.Empty, UserRole.Innovator, null),
        generatedPassword);
    return user is null
        ? Results.Conflict("An account with this email already exists.")
        : Results.Ok(auth.CreateSession(user));
});

app.MapPost("/api/auth/logout", [Authorize] (HttpRequest request, AuthSessionService auth) =>
{
    var token = request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
    auth.Revoke(token);
    return Results.NoContent();
});

app.MapGet("/api/dashboard", [Authorize(Policy = "DashboardViewer")] async (ClaimsPrincipal principal, DashboardService dashboard) =>
{
    var organizationId = principal.IsInRole(UserRole.OrganizationOwner.ToString())
        ? GetOrganizationId(principal)
        : null;
    if (principal.IsInRole(UserRole.OrganizationOwner.ToString()) && !organizationId.HasValue) return Results.Forbid();
    return Results.Ok(await dashboard.GetSummaryAsync(organizationId));
});

app.MapGet("/api/problems", [AllowAnonymous] async (string? search, int? categoryId, ProblemStatus? status, ClaimsPrincipal principal, ChallengeService challenges) =>
{
    var problems = await challenges.SearchProblemsAsync(search, categoryId, status);
    if (principal.IsInRole(UserRole.OrganizationOwner.ToString()))
    {
        var organizationId = GetOrganizationId(principal);
        if (!organizationId.HasValue) return Results.Forbid();
        problems = problems.Where(problem => problem.OrganizationId == organizationId && problem.Status != ProblemStatus.Archived).ToList();
    }
    else if (principal.IsInRole(UserRole.Evaluator.ToString()))
    {
        problems = problems.Where(problem => problem.Status != ProblemStatus.Archived).ToList();
    }
    else if (!principal.IsInRole(UserRole.Admin.ToString()))
    {
        problems = problems.Where(problem => problem.Status == ProblemStatus.Open).ToList();
    }

    return Results.Ok(problems);
});

app.MapPost("/api/problems", [Authorize(Policy = "ChallengePublisher")] async (CreateProblemRequest request, ClaimsPrincipal principal, ChallengeService challenges, IAppRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
        return Results.BadRequest("Challenge title and description are required.");
    if (request.BountyAmount < 0)
        return Results.BadRequest("The bounty amount cannot be negative.");
    if (principal.IsInRole(UserRole.OrganizationOwner.ToString()))
    {
        var organizationId = GetOrganizationId(principal);
        if (!organizationId.HasValue) return Results.Forbid();
        if (request.OrganizationId != organizationId) return Results.Forbid();
    }

    if (!(await repository.GetOrganizationsAsync()).Any(organization => organization.Id == request.OrganizationId))
        return Results.BadRequest("Select a valid organization.");
    if (!(await repository.GetCategoriesAsync()).Any(category => category.Id == request.CategoryId))
        return Results.BadRequest("Select a valid category.");
    var skillIds = (await repository.GetSkillsAsync()).Select(skill => skill.Id).ToHashSet();
    if (request.RequiredSkillIds is null || request.RequiredSkillIds.Any(id => !skillIds.Contains(id)))
        return Results.BadRequest("Select valid required skills.");

    var problem = await challenges.CreateProblemAsync(request);
    return problem is null
        ? Results.Conflict("A challenge with that title already exists for this organization.")
        : Results.Created($"/api/problems/{problem.Id}", problem);
});

app.MapPut("/api/problems/{problemId:int}", [Authorize(Policy = "ChallengePublisher")] async (
    int problemId,
    UpdateProblemRequest request,
    ClaimsPrincipal principal,
    IAppRepository repository) =>
{
    var existing = await repository.FindProblemAsync(problemId);
    if (existing is null) return Results.NotFound();
    if (existing.Status == ProblemStatus.Archived) return Results.Conflict("Archived challenges cannot be edited.");
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
        return Results.BadRequest("Challenge title and description are required.");
    if (request.BountyAmount < 0)
        return Results.BadRequest("The bounty amount cannot be negative.");

    if (principal.IsInRole(UserRole.OrganizationOwner.ToString()))
    {
        var organizationId = GetOrganizationId(principal);
        if (!organizationId.HasValue || existing.OrganizationId != organizationId || request.OrganizationId != organizationId)
            return Results.Forbid();
    }

    var organizations = await repository.GetOrganizationsAsync();
    if (!organizations.Any(organization => organization.Id == request.OrganizationId))
        return Results.BadRequest("Select a valid organization.");
    var categories = await repository.GetCategoriesAsync();
    if (!categories.Any(category => category.Id == request.CategoryId))
        return Results.BadRequest("Select a valid category.");
    var skillIds = (await repository.GetSkillsAsync()).Select(skill => skill.Id).ToHashSet();
    if (request.RequiredSkillIds is null || request.RequiredSkillIds.Any(id => !skillIds.Contains(id)))
        return Results.BadRequest("Select valid required skills.");

    var updated = await repository.UpdateProblemAsync(existing with
    {
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        OrganizationId = request.OrganizationId,
        CategoryId = request.CategoryId,
        Deadline = request.Deadline,
        RequiredSkillIds = request.RequiredSkillIds,
        BountyAmount = request.BountyAmount,
        OffersEmployment = request.OffersEmployment
    });
    return updated is null
        ? Results.Conflict("A challenge with that title already exists for this organization.")
        : Results.Ok(updated);
});

app.MapPost("/api/problems/{problemId:int}/archive", [Authorize(Policy = "AdministratorOnly")] async (
    int problemId,
    IAppRepository repository) =>
{
    var problem = await repository.FindProblemAsync(problemId);
    if (problem is null) return Results.NotFound();
    if (problem.Status == ProblemStatus.Archived) return Results.Conflict("This challenge has already been archived.");
    var archived = await repository.UpdateProblemStatusAsync(problemId, ProblemStatus.Archived);
    return archived is null ? Results.NotFound() : Results.Ok(archived);
});

app.MapGet("/api/category-suggestions", [Authorize] async (
    ClaimsPrincipal principal,
    IAppRepository repository) =>
{
    var suggestions = await repository.GetCategorySuggestionsAsync();
    if (!principal.IsInRole(UserRole.Admin.ToString()))
    {
        var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        suggestions = suggestions.Where(item => item.SubmittedByUserId == userId).ToList();
    }

    var users = (await repository.GetUsersAsync()).ToDictionary(user => user.Id, user => user.FullName);
    return Results.Ok(suggestions.Select(item => new
    {
        item.Id,
        item.Name,
        item.Description,
        item.SubmittedAt,
        item.Status,
        submittedBy = users.GetValueOrDefault(item.SubmittedByUserId, "Former member")
    }));
});

app.MapPost("/api/category-suggestions", [Authorize] async (
    CreateCategorySuggestionRequest request,
    ClaimsPrincipal principal,
    IAppRepository repository) =>
{
    var name = request.Name?.Trim();
    var description = request.Description?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length is < 3 or > 80)
        return Results.BadRequest("Category name must be between 3 and 80 characters.");
    if (string.IsNullOrWhiteSpace(description) || description.Length is < 5 or > 300)
        return Results.BadRequest("Category description must be between 5 and 300 characters.");

    var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var suggestion = await repository.AddCategorySuggestionAsync(new CategorySuggestion(
        0, name, description, userId, DateTime.UtcNow, CategorySuggestionStatus.Pending));
    return suggestion is null
        ? Results.Conflict("That category already exists or is already awaiting review.")
        : Results.Created($"/api/category-suggestions/{suggestion.Id}", suggestion);
});

app.MapPatch("/api/category-suggestions/{suggestionId:int}/status", [Authorize(Policy = "AdministratorOnly")] async (
    int suggestionId,
    ReviewCategorySuggestionRequest request,
    IAppRepository repository) =>
{
    if (request.Status is not (CategorySuggestionStatus.Approved or CategorySuggestionStatus.Rejected))
        return Results.BadRequest("Choose approved or rejected.");

    var result = await repository.ReviewCategorySuggestionAsync(suggestionId, request.Status);
    return result switch
    {
        CategorySuggestionReviewResult.Updated => Results.NoContent(),
        CategorySuggestionReviewResult.NotFound => Results.NotFound(),
        CategorySuggestionReviewResult.AlreadyReviewed => Results.Conflict("This suggestion has already been reviewed."),
        CategorySuggestionReviewResult.DuplicateCategory => Results.Conflict("A category with this name already exists."),
        _ => Results.Problem("The category suggestion could not be reviewed.")
    };
});

app.MapPost("/api/users", [Authorize(Policy = "AdministratorOnly")] async (
    CreateUserRequest request,
    IAppRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
        return Results.BadRequest("Name, email, and a password of at least 8 characters are required.");
    if (!Enum.IsDefined(request.Role)) return Results.BadRequest("Select a valid role.");
    if (request.Role == UserRole.OrganizationOwner && !request.OrganizationId.HasValue)
        return Results.BadRequest("Organization accounts must be assigned to an organization.");
    if (request.OrganizationId.HasValue &&
        !(await repository.GetOrganizationsAsync()).Any(organization => organization.Id == request.OrganizationId.Value))
        return Results.BadRequest("Select a valid organization.");

    var user = await repository.AddUserAsync(
        new User(0, request.FullName.Trim(), request.Email.Trim(), string.Empty, request.Role, request.OrganizationId),
        request.Password);
    return user is null
        ? Results.Conflict("A member with that email already exists.")
        : Results.Created($"/api/users/{user.Id}", AuthSessionService.ToProfile(user));
});

app.MapPatch("/api/users/{userId:int}/status", [Authorize(Policy = "AdministratorOnly")] async (
    int userId,
    UpdateUserStatusRequest request,
    ClaimsPrincipal principal,
    IAppRepository repository,
    AuthSessionService auth) =>
{
    var currentUserId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    if (userId == currentUserId && !request.IsActive)
        return Results.Conflict("You cannot deactivate your own administrator account.");

    var user = await repository.UpdateUserStatusAsync(userId, request.IsActive);
    if (user is null) return Results.NotFound();
    if (!request.IsActive) auth.RevokeUserSessions(userId);
    return Results.Ok(AuthSessionService.ToProfile(user));
});

app.MapPost("/api/organizations", [Authorize(Policy = "AdministratorOnly")] async (
    CreateOrganizationRequest request,
    IAppRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sector) || string.IsNullOrWhiteSpace(request.Location))
        return Results.BadRequest("Organization name, sector, and location are required.");
    if ((await repository.GetOrganizationsAsync()).Any(organization =>
        organization.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict("An organization with that name already exists.");

    var organization = await repository.AddOrganizationAsync(new Organization(
        0, request.Name.Trim(), request.Sector.Trim(), request.Location.Trim()));
    return Results.Created($"/api/organizations/{organization.Id}", organization);
});

app.MapPut("/api/organizations/{organizationId:int}", [Authorize(Policy = "AdministratorOnly")] async (
    int organizationId,
    UpdateOrganizationRequest request,
    IAppRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sector) || string.IsNullOrWhiteSpace(request.Location))
        return Results.BadRequest("Organization name, sector, and location are required.");
    if ((await repository.GetOrganizationsAsync()).Any(organization =>
        organization.Id != organizationId &&
        organization.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict("An organization with that name already exists.");

    var updated = await repository.UpdateOrganizationAsync(new Organization(
        organizationId, request.Name.Trim(), request.Sector.Trim(), request.Location.Trim()));
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/organizations/{organizationId:int}", [Authorize(Policy = "AdministratorOnly")] async (
    int organizationId,
    IAppRepository repository) =>
{
    if (!(await repository.GetOrganizationsAsync()).Any(organization => organization.Id == organizationId))
        return Results.NotFound();
    if (!await repository.DeleteOrganizationAsync(organizationId))
        return Results.Conflict("Organizations with members or challenges cannot be removed.");
    return Results.NoContent();
});

app.MapGet("/api/proposals", [Authorize] async (string? search, ProposalStatus? status, ClaimsPrincipal principal, ProposalService proposals) =>
{
    var userId = principal.IsInRole(UserRole.Innovator.ToString())
        ? int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!)
        : (int?)null;
    var organizationId = principal.IsInRole(UserRole.OrganizationOwner.ToString())
        ? GetOrganizationId(principal)
        : (int?)null;
    if (principal.IsInRole(UserRole.OrganizationOwner.ToString()) && !organizationId.HasValue) return Results.Forbid();
    return Results.Ok(await proposals.SearchProposalsAsync(search, status, userId, organizationId));
});

app.MapPost("/api/proposals", [Authorize(Policy = "InnovatorOnly")] async (CreateProposalRequest request, ClaimsPrincipal principal, ProposalService proposals) =>
{
    var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var proposal = await proposals.SubmitProposalAsync(request with { UserId = userId });
    if (proposal is null) return Results.BadRequest("Choose an open challenge before submitting a proposal.");
    return Results.Created($"/api/proposals/{proposal.Id}", proposal);
});

app.MapPatch("/api/proposals/{proposalId:int}/status", [Authorize(Policy = "ChallengePublisher")] async (
    int proposalId,
    UpdateProposalStatusRequest request,
    ClaimsPrincipal principal,
    IAppRepository repository) =>
{
    var proposal = await repository.FindProposalAsync(proposalId);
    if (proposal is null) return Results.NotFound();

    if (principal.IsInRole(UserRole.OrganizationOwner.ToString()))
    {
        var problem = await repository.FindProblemAsync(proposal.ProblemId);
        var organizationId = GetOrganizationId(principal);
        if (!organizationId.HasValue) return Results.Forbid();
        if (problem?.OrganizationId != organizationId) return Results.Forbid();
    }

    var updated = await repository.UpdateProposalStatusAsync(proposalId, request.Status);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapPost("/api/proposals/{proposalId:int}/solutions", [Authorize] async (
    int proposalId,
    SubmitSolutionRequest request,
    ClaimsPrincipal principal,
    ProposalService proposals) =>
{
    if (!principal.IsInRole(UserRole.Innovator.ToString())) return Results.Forbid();
    var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var solution = await proposals.SubmitSolutionAsync(proposalId, userId, request);
    return solution is null ? Results.NotFound() : Results.Created($"/api/solutions/{solution.Id}", solution);
});

app.MapPost("/api/evaluations", [Authorize(Policy = "EvaluatorOnly")] async (
    CreateEvaluationRequest request,
    ClaimsPrincipal principal,
    EvaluationService evaluations) =>
{
    var evaluatorUserId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = await evaluations.EvaluateAsync(request with { EvaluatorUserId = evaluatorUserId });
    return result is null ? Results.BadRequest("A valid solution and evaluator are required.") : Results.Created("/api/evaluations", result);
});

app.MapGet("/api/lookups", async (ClaimsPrincipal principal, IAppRepository repository) =>
{
    var isAdmin = principal.IsInRole(UserRole.Admin.ToString());
    var isOrganizationOwner = principal.IsInRole(UserRole.OrganizationOwner.ToString());
    var organizationId = isOrganizationOwner ? GetOrganizationId(principal) : null;
    if (isOrganizationOwner && !organizationId.HasValue) return Results.Forbid();

    IReadOnlyList<UserProfile> users = isAdmin || isOrganizationOwner
        ? (await repository.GetUsersAsync())
            .Where(user => isAdmin || user.OrganizationId == organizationId)
            .Select(AuthSessionService.ToProfile)
            .ToList()
        : Array.Empty<UserProfile>();
    var organizations = (await repository.GetOrganizationsAsync())
        .Where(organization => !isOrganizationOwner || organization.Id == organizationId)
        .ToList();

    return Results.Ok(new
    {
        users,
        organizations,
        categories = await repository.GetCategoriesAsync(),
        skills = await repository.GetSkillsAsync(),
        rewards = await repository.GetRewardsAsync()
    });
});

app.MapFallbackToFile("index.html");

app.Run();

static int? GetOrganizationId(ClaimsPrincipal principal) =>
    int.TryParse(principal.FindFirstValue("organization_id"), out var organizationId) ? organizationId : null;

static string? GetAuthItem(IDictionary<string, string?> items, string key) =>
    items.TryGetValue(key, out var value) ? value : null;
