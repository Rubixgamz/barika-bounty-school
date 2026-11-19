using School_Project.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;

namespace School_Project.Data;

public sealed class InMemoryAppRepository : IAppRepository
{
    private static readonly PasswordHasher<User> PasswordHasher = new();
    private static readonly Category[] DefaultCategories =
    [
        new(1, "Health", "Healthcare access, public health, mental health, and wellbeing."),
        new(2, "Education", "Schools, learning, skills development, and educational access."),
        new(3, "Environment", "Conservation, pollution, waste, and protection of natural resources."),
        new(4, "Agriculture & Food Systems", "Farming, food production, food security, and supply chains."),
        new(5, "Arts, Culture & Heritage", "Creative industries, cultural expression, and heritage preservation."),
        new(6, "Business & Entrepreneurship", "Business growth, enterprise development, and market access."),
        new(7, "Climate & Disaster Resilience", "Climate adaptation, disaster preparedness, and recovery."),
        new(8, "Community Development", "Local services, participation, and community-led development."),
        new(9, "Digital Technology & Data", "Software, connectivity, digital services, data, and cybersecurity."),
        new(10, "Employment & Livelihoods", "Jobs, workforce development, and sustainable livelihoods."),
        new(11, "Energy", "Renewable energy, energy access, efficiency, and storage."),
        new(12, "Finance & Financial Inclusion", "Financial services, savings, credit, and economic inclusion."),
        new(13, "Governance & Civic Engagement", "Public institutions, civic participation, and accountability."),
        new(14, "Housing & Urban Development", "Affordable housing, urban planning, and safer neighborhoods."),
        new(15, "Infrastructure & Public Works", "Public facilities, construction, and essential infrastructure."),
        new(16, "Manufacturing & Industry", "Production, industrial processes, and responsible manufacturing."),
        new(17, "Public Safety & Justice", "Safety, emergency response, legal access, and justice services."),
        new(18, "Science & Research", "Scientific research, experimentation, and applied innovation."),
        new(19, "Social Protection & Inclusion", "Disability access, social services, equality, and support for vulnerable groups."),
        new(20, "Sports & Recreation", "Sports participation, recreation, and physical activity."),
        new(21, "Tourism & Hospitality", "Tourism, travel, hospitality, and local visitor economies."),
        new(22, "Transportation & Mobility", "Public transit, roads, logistics, and accessible mobility."),
        new(23, "Water & Sanitation", "Clean water, sanitation, hygiene, and water management."),
        new(24, "Gender Equality & Women's Empowerment", "Gender equity, safety, and women's participation and opportunity."),
        new(25, "Youth & Child Development", "Child wellbeing, youth services, and opportunities for young people.")
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly object _lock = new();
    private readonly string _dataFilePath;
    private readonly List<User> _users = [];
    private readonly List<Organization> _organizations = [];
    private readonly List<Category> _categories = [];
    private readonly List<CategorySuggestion> _categorySuggestions = [];
    private readonly List<Skill> _skills = [];
    private readonly List<Reward> _rewards = [];
    private readonly List<Problem> _problems = [];
    private readonly List<Proposal> _proposals = [];
    private readonly List<Solution> _solutions = [];
    private readonly List<Evaluation> _evaluations = [];
    private readonly List<ImplementationRecord> _implementationRecords = [];

    public InMemoryAppRepository(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configuredPath = Environment.GetEnvironmentVariable("BARIKA_DATA_FILE");
        _dataFilePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "barika-data.json")
            : configuredPath);

        if (File.Exists(_dataFilePath))
        {
            LoadData();
            return;
        }

        SeedDefaults(
            environment.IsDevelopment(),
            configuration["BARIKA_ADMIN_EMAIL"],
            configuration["BARIKA_ADMIN_PASSWORD"]);
        SaveData();
    }

    private void SeedDefaults(bool includeDemoData, string? adminEmail, string? adminPassword)
    {
        if (!includeDemoData &&
            (string.IsNullOrWhiteSpace(adminEmail) ||
             string.IsNullOrWhiteSpace(adminPassword) ||
             adminPassword.Length < 12))
        {
            throw new InvalidOperationException("Set BARIKA_ADMIN_EMAIL and a BARIKA_ADMIN_PASSWORD of at least 12 characters before initializing production data.");
        }

        _categories.AddRange(DefaultCategories);
        _skills.AddRange([
            new(1, "Software Development"),
            new(2, "Data Analysis"),
            new(3, "Project Management"),
            new(4, "Community Research")
        ]);

        if (!includeDemoData)
        {
            _users.Add(CreateUser(1, "Platform Administrator", adminEmail!, adminPassword!, UserRole.Admin, null));
            return;
        }

        _organizations.AddRange([
            new(1, "Nairobi Innovation Hub", "Technology", "Nairobi"),
            new(2, "Green Cities Initiative", "Environment", "Mombasa")
        ]);
        _users.AddRange([
            CreateUser(1, "Admin User", "admin@school.local", "admin123", UserRole.Admin, 1),
            CreateUser(2, "Amina Otieno", "amina@school.local", "pass123", UserRole.Innovator, null),
            CreateUser(3, "Victor Mwangi", "victor@school.local", "pass123", UserRole.Evaluator, 1),
            CreateUser(4, "Green Cities Manager", "company@school.local", "company123", UserRole.OrganizationOwner, 2),
            CreateUser(5, "Samira Njeri", "samira@school.local", "pass123", UserRole.Innovator, null)
        ]);
        _problems.AddRange([
            new(1, "Digital proposal tracking", "Create a transparent way to receive, review, and implement innovation proposals.", 1, 2, ProblemStatus.Open, DateTime.UtcNow.AddDays(30), [1, 3], 50000),
            new(2, "Waste collection reporting", "Help communities report overflowing bins and track clean-up implementation.", 2, 3, ProblemStatus.UnderReview, DateTime.UtcNow.AddDays(18), [1, 2, 4], 80000)
        ]);
        _rewards.AddRange([
            new(1, "Prototype Grant", 50000, "KES", 1),
            new(2, "Pilot Support", 80000, "KES", 2)
        ]);
        _proposals.Add(new(1, 1, 2, "ChallengeHub", "A web platform with proposal intake, review workflow, dashboards, and implementation records.", ProposalStatus.Shortlisted, DateTime.UtcNow.AddDays(-4)));
        _proposals.Add(new(2, 2, 2, "Community cleanup reporting", "A mobile-friendly reporting and coordination tool for neighborhood cleanup teams.", ProposalStatus.Submitted, DateTime.UtcNow.AddDays(-1)));
        _proposals.Add(new(3, 1, 5, "Learning access toolkit", "A community-centered toolkit for making learning resources easier to find and use.", ProposalStatus.Submitted, DateTime.UtcNow.AddHours(-12)));
        _solutions.Add(new(1, 1, "Build the platform in three layers with ASP.NET Core APIs, a browser dashboard, and SQL-ready records.", "https://example.com/demo", DateTime.UtcNow.AddDays(-2)));
        _evaluations.Add(new(1, 1, 3, 88, "Strong fit for the problem and clear implementation plan.", DateTime.UtcNow.AddDays(-1)));
        _implementationRecords.Add(new(1, 1, "Prototype complete", "In Progress", DateTime.UtcNow.AddHours(-8)));
    }

    public Task<IReadOnlyList<User>> GetUsersAsync() => Snapshot(_users);
    public Task<IReadOnlyList<Organization>> GetOrganizationsAsync() => Snapshot(_organizations);
    public Task<IReadOnlyList<Category>> GetCategoriesAsync() => Snapshot(_categories);
    public Task<IReadOnlyList<CategorySuggestion>> GetCategorySuggestionsAsync() => Snapshot(_categorySuggestions);
    public Task<IReadOnlyList<Skill>> GetSkillsAsync() => Snapshot(_skills);
    public Task<IReadOnlyList<Reward>> GetRewardsAsync() => Snapshot(_rewards);
    public Task<IReadOnlyList<Problem>> GetProblemsAsync() => Snapshot(_problems);
    public Task<IReadOnlyList<Proposal>> GetProposalsAsync() => Snapshot(_proposals);
    public Task<IReadOnlyList<Solution>> GetSolutionsAsync() => Snapshot(_solutions);
    public Task<IReadOnlyList<Evaluation>> GetEvaluationsAsync() => Snapshot(_evaluations);
    public Task<IReadOnlyList<ImplementationRecord>> GetImplementationRecordsAsync() => Snapshot(_implementationRecords);

    public Task<User?> FindUserByEmailAsync(string email)
    {
        lock (_lock) return Task.FromResult(_users.FirstOrDefault(user => user.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<User?> FindUserAsync(int id)
    {
        lock (_lock) return Task.FromResult(_users.FirstOrDefault(user => user.Id == id));
    }

    public Task<Problem?> FindProblemAsync(int id)
    {
        lock (_lock) return Task.FromResult(_problems.FirstOrDefault(problem => problem.Id == id));
    }

    public Task<Proposal?> FindProposalAsync(int id)
    {
        lock (_lock) return Task.FromResult(_proposals.FirstOrDefault(proposal => proposal.Id == id));
    }

    public Task<Solution?> FindSolutionAsync(int id)
    {
        lock (_lock) return Task.FromResult(_solutions.FirstOrDefault(solution => solution.Id == id));
    }

    public Task<User?> AddUserAsync(User user, string password)
    {
        lock (_lock)
        {
            if (_users.Any(existing => existing.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<User?>(null);

            var newUser = user with
            {
                Id = NextId(_users.Select(item => item.Id)),
                PasswordHash = PasswordHasher.HashPassword(user, password),
                IsActive = true
            };
            _users.Add(newUser);
            SaveData();
            return Task.FromResult<User?>(newUser);
        }
    }

    public Task<OrganizationOwnerRegistration?> RegisterOrganizationOwnerAsync(User user, string password, Organization organization, bool isActive)
    {
        lock (_lock)
        {
            if (_users.Any(existing => existing.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)) ||
                _organizations.Any(existing => existing.Name.Equals(organization.Name, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<OrganizationOwnerRegistration?>(null);

            var savedOrganization = organization with
            {
                Id = NextId(_organizations.Select(item => item.Id))
            };
            var userWithOrganization = user with
            {
                Id = NextId(_users.Select(item => item.Id)),
                OrganizationId = savedOrganization.Id
            };
            var savedUser = userWithOrganization with
            {
                PasswordHash = PasswordHasher.HashPassword(userWithOrganization, password),
                IsActive = isActive
            };

            _organizations.Add(savedOrganization);
            _users.Add(savedUser);
            SaveData();
            return Task.FromResult<OrganizationOwnerRegistration?>(
                new OrganizationOwnerRegistration(savedUser, savedOrganization));
        }
    }

    public Task<User?> UpdateUserStatusAsync(int userId, bool isActive)
    {
        lock (_lock)
        {
            var index = _users.FindIndex(user => user.Id == userId);
            if (index < 0) return Task.FromResult<User?>(null);
            _users[index] = _users[index] with { IsActive = isActive };
            SaveData();
            return Task.FromResult<User?>(_users[index]);
        }
    }

    public Task<Organization> AddOrganizationAsync(Organization organization)
    {
        lock (_lock)
        {
            var saved = organization with { Id = NextId(_organizations.Select(item => item.Id)) };
            _organizations.Add(saved);
            SaveData();
            return Task.FromResult(saved);
        }
    }

    public Task<Organization?> UpdateOrganizationAsync(Organization organization)
    {
        lock (_lock)
        {
            var index = _organizations.FindIndex(item => item.Id == organization.Id);
            if (index < 0) return Task.FromResult<Organization?>(null);
            _organizations[index] = organization;
            SaveData();
            return Task.FromResult<Organization?>(organization);
        }
    }

    public Task<bool> DeleteOrganizationAsync(int organizationId)
    {
        lock (_lock)
        {
            if (_users.Any(user => user.OrganizationId == organizationId) ||
                _problems.Any(problem => problem.OrganizationId == organizationId))
                return Task.FromResult(false);

            var removed = _organizations.RemoveAll(organization => organization.Id == organizationId) > 0;
            if (removed) SaveData();
            return Task.FromResult(removed);
        }
    }

    public Task<CategorySuggestion?> AddCategorySuggestionAsync(CategorySuggestion suggestion)
    {
        lock (_lock)
        {
            if (_categories.Any(category => category.Name.Equals(suggestion.Name, StringComparison.OrdinalIgnoreCase)) ||
                _categorySuggestions.Any(existing =>
                    existing.Status == CategorySuggestionStatus.Pending &&
                    existing.Name.Equals(suggestion.Name, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<CategorySuggestion?>(null);

            var saved = suggestion with { Id = NextId(_categorySuggestions.Select(item => item.Id)) };
            _categorySuggestions.Add(saved);
            SaveData();
            return Task.FromResult<CategorySuggestion?>(saved);
        }
    }

    public Task<CategorySuggestionReviewResult> ReviewCategorySuggestionAsync(int suggestionId, CategorySuggestionStatus status)
    {
        lock (_lock)
        {
            var index = _categorySuggestions.FindIndex(item => item.Id == suggestionId);
            if (index < 0) return Task.FromResult(CategorySuggestionReviewResult.NotFound);

            var suggestion = _categorySuggestions[index];
            if (suggestion.Status != CategorySuggestionStatus.Pending)
                return Task.FromResult(CategorySuggestionReviewResult.AlreadyReviewed);

            if (status == CategorySuggestionStatus.Approved)
            {
                if (_categories.Any(category => category.Name.Equals(suggestion.Name, StringComparison.OrdinalIgnoreCase)))
                    return Task.FromResult(CategorySuggestionReviewResult.DuplicateCategory);

                _categories.Add(new Category(
                    NextId(_categories.Select(category => category.Id)),
                    suggestion.Name,
                    suggestion.Description));
            }

            _categorySuggestions[index] = suggestion with { Status = status };
            SaveData();
            return Task.FromResult(CategorySuggestionReviewResult.Updated);
        }
    }

    public Task<Problem?> AddProblemAsync(Problem problem)
    {
        lock (_lock)
        {
            if (_problems.Any(existing =>
                existing.OrganizationId == problem.OrganizationId &&
                existing.Title.Equals(problem.Title, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<Problem?>(null);

            var saved = problem with { Id = NextId(_problems.Select(item => item.Id)) };
            _problems.Add(saved);
            SaveData();
            return Task.FromResult<Problem?>(saved);
        }
    }

    public Task<Problem?> UpdateProblemAsync(Problem problem)
    {
        lock (_lock)
        {
            var index = _problems.FindIndex(item => item.Id == problem.Id);
            if (index < 0) return Task.FromResult<Problem?>(null);
            if (_problems.Any(existing =>
                existing.Id != problem.Id &&
                existing.OrganizationId == problem.OrganizationId &&
                existing.Title.Equals(problem.Title, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<Problem?>(null);

            _problems[index] = problem;
            SaveData();
            return Task.FromResult<Problem?>(problem);
        }
    }

    public Task<Proposal> AddProposalAsync(Proposal proposal)
    {
        lock (_lock)
        {
            var saved = proposal with { Id = NextId(_proposals.Select(item => item.Id)) };
            _proposals.Add(saved);
            SaveData();
            return Task.FromResult(saved);
        }
    }

    public Task<Solution> AddSolutionAsync(Solution solution)
    {
        lock (_lock)
        {
            var saved = solution with { Id = NextId(_solutions.Select(item => item.Id)) };
            _solutions.Add(saved);
            SaveData();
            return Task.FromResult(saved);
        }
    }

    public Task<Evaluation> AddEvaluationAsync(Evaluation evaluation)
    {
        lock (_lock)
        {
            var saved = evaluation with { Id = NextId(_evaluations.Select(item => item.Id)) };
            _evaluations.Add(saved);
            SaveData();
            return Task.FromResult(saved);
        }
    }

    public Task<Proposal?> UpdateProposalStatusAsync(int proposalId, ProposalStatus status)
    {
        lock (_lock)
        {
            var index = _proposals.FindIndex(item => item.Id == proposalId);
            if (index < 0) return Task.FromResult<Proposal?>(null);
            _proposals[index] = _proposals[index] with { Status = status };
            SaveData();
            return Task.FromResult<Proposal?>(_proposals[index]);
        }
    }

    public Task<Problem?> UpdateProblemStatusAsync(int problemId, ProblemStatus status)
    {
        lock (_lock)
        {
            var index = _problems.FindIndex(item => item.Id == problemId);
            if (index < 0) return Task.FromResult<Problem?>(null);
            _problems[index] = _problems[index] with { Status = status };
            SaveData();
            return Task.FromResult<Problem?>(_problems[index]);
        }
    }

    private Task<IReadOnlyList<T>> Snapshot<T>(List<T> source)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<T>>(source.ToList());
    }

    private static int NextId(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;

    private static User CreateUser(int id, string fullName, string email, string password, UserRole role, int? organizationId)
    {
        var user = new User(id, fullName, email, string.Empty, role, organizationId);
        return user with { PasswordHash = PasswordHasher.HashPassword(user, password) };
    }

    private void LoadData()
    {
        var json = File.ReadAllText(_dataFilePath);
        var data = JsonSerializer.Deserialize<RepositoryData>(json, JsonOptions)
            ?? throw new InvalidDataException($"The application data file '{_dataFilePath}' is empty or invalid.");
        _users.AddRange(data.Users);
        _organizations.AddRange(data.Organizations);
        _categories.AddRange(data.Categories);
        _categorySuggestions.AddRange(data.CategorySuggestions ?? []);
        _skills.AddRange(data.Skills);
        _rewards.AddRange(data.Rewards);
        _problems.AddRange(data.Problems);
        _proposals.AddRange(data.Proposals);
        _solutions.AddRange(data.Solutions);
        _evaluations.AddRange(data.Evaluations);
        _implementationRecords.AddRange(data.ImplementationRecords);

        var categoriesAdded = false;
        foreach (var category in DefaultCategories)
        {
            if (_categories.Any(existing => existing.Name.Equals(category.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            _categories.Add(category with { Id = NextId(_categories.Select(item => item.Id)) });
            categoriesAdded = true;
        }
        if (categoriesAdded) SaveData();
    }

    private void SaveData()
    {
        var directory = Path.GetDirectoryName(_dataFilePath)
            ?? throw new InvalidOperationException("The application data path must include a directory.");
        Directory.CreateDirectory(directory);

        var data = new RepositoryData(
            _users, _organizations, _categories, _categorySuggestions, _skills, _rewards, _problems,
            _proposals, _solutions, _evaluations, _implementationRecords);
        var temporaryPath = $"{_dataFilePath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temporaryPath, _dataFilePath, overwrite: true);
    }

    private sealed record RepositoryData(
        List<User> Users,
        List<Organization> Organizations,
        List<Category> Categories,
        List<CategorySuggestion>? CategorySuggestions,
        List<Skill> Skills,
        List<Reward> Rewards,
        List<Problem> Problems,
        List<Proposal> Proposals,
        List<Solution> Solutions,
        List<Evaluation> Evaluations,
        List<ImplementationRecord> ImplementationRecords);
}
