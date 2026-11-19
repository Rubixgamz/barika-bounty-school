namespace School_Project.Models;

public enum UserRole
{
    Admin,
    Innovator,
    Evaluator,
    OrganizationOwner
}

public enum ProblemStatus
{
    Draft,
    Open,
    UnderReview,
    Solved,
    Archived
}

public enum ProposalStatus
{
    Submitted,
    Shortlisted,
    Rejected,
    Approved,
    Implemented
}

public enum CategorySuggestionStatus
{
    Pending,
    Approved,
    Rejected
}

public enum CategorySuggestionReviewResult
{
    Updated,
    NotFound,
    AlreadyReviewed,
    DuplicateCategory
}

public sealed record User(int Id, string FullName, string Email, string PasswordHash, UserRole Role, int? OrganizationId, bool IsActive = true);
public sealed record UserProfile(int Id, string FullName, string Email, UserRole Role, int? OrganizationId, bool IsActive);
public sealed record OrganizationOwnerRegistration(User User, Organization Organization);
public sealed record ProposalAuthor(int Id, string FullName);
public sealed record Organization(int Id, string Name, string Sector, string Location);
public sealed record Skill(int Id, string Name);
public sealed record Category(int Id, string Name, string Description);
public sealed record CategorySuggestion(int Id, string Name, string Description, int SubmittedByUserId, DateTime SubmittedAt, CategorySuggestionStatus Status);
public sealed record Reward(int Id, string Title, decimal Amount, string Currency, int ProblemId);

public sealed record Problem(
    int Id,
    string Title,
    string Description,
    int OrganizationId,
    int CategoryId,
    ProblemStatus Status,
    DateTime Deadline,
    List<int> RequiredSkillIds,
    decimal? BountyAmount = null,
    bool OffersEmployment = false);

public sealed record Proposal(
    int Id,
    int ProblemId,
    int UserId,
    string Title,
    string Summary,
    ProposalStatus Status,
    DateTime SubmittedAt);

public sealed record Solution(
    int Id,
    int ProposalId,
    string ImplementationPlan,
    string EvidenceUrl,
    DateTime SubmittedAt);

public sealed record Evaluation(
    int Id,
    int SolutionId,
    int EvaluatorUserId,
    int Score,
    string Comments,
    DateTime EvaluatedAt);

public sealed record ImplementationRecord(
    int Id,
    int SolutionId,
    string Milestone,
    string Status,
    DateTime UpdatedAt);

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, UserProfile User);
public sealed record RegisterRequest(string FullName, string Email, string Password, UserRole Role, string? OrganizationName, string? Sector, string? Location);
public sealed record CreateUserRequest(string FullName, string Email, string Password, UserRole Role, int? OrganizationId);
public sealed record UpdateUserStatusRequest(bool IsActive);
public sealed record CreateOrganizationRequest(string Name, string Sector, string Location);
public sealed record UpdateOrganizationRequest(string Name, string Sector, string Location);
public sealed record CreateCategorySuggestionRequest(string Name, string Description);
public sealed record ReviewCategorySuggestionRequest(CategorySuggestionStatus Status);
public sealed record CreateProblemRequest(string Title, string Description, int OrganizationId, int CategoryId, DateTime Deadline, List<int> RequiredSkillIds, decimal BountyAmount, bool OffersEmployment = false);
public sealed record UpdateProblemRequest(string Title, string Description, int OrganizationId, int CategoryId, DateTime Deadline, List<int> RequiredSkillIds, decimal BountyAmount, bool OffersEmployment = false);
public sealed record CreateProposalRequest(int ProblemId, int UserId, string Title, string Summary);
public sealed record SubmitSolutionRequest(string ImplementationPlan, string EvidenceUrl);
public sealed record CreateEvaluationRequest(int SolutionId, int EvaluatorUserId, int Score, string Comments);
public sealed record UpdateProposalStatusRequest(ProposalStatus Status);
