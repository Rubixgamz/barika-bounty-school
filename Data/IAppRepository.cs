using School_Project.Models;

namespace School_Project.Data;

public interface IAppRepository
{
    Task<IReadOnlyList<User>> GetUsersAsync();
    Task<IReadOnlyList<Organization>> GetOrganizationsAsync();
    Task<IReadOnlyList<Category>> GetCategoriesAsync();
    Task<IReadOnlyList<CategorySuggestion>> GetCategorySuggestionsAsync();
    Task<IReadOnlyList<Skill>> GetSkillsAsync();
    Task<IReadOnlyList<Reward>> GetRewardsAsync();
    Task<IReadOnlyList<Problem>> GetProblemsAsync();
    Task<IReadOnlyList<Proposal>> GetProposalsAsync();
    Task<IReadOnlyList<Solution>> GetSolutionsAsync();
    Task<IReadOnlyList<Evaluation>> GetEvaluationsAsync();
    Task<IReadOnlyList<ImplementationRecord>> GetImplementationRecordsAsync();
    Task<User?> FindUserByEmailAsync(string email);
    Task<User?> AddUserAsync(User user, string password);
    Task<OrganizationOwnerRegistration?> RegisterOrganizationOwnerAsync(User user, string password, Organization organization, bool isActive);
    Task<User?> UpdateUserStatusAsync(int userId, bool isActive);
    Task<Organization> AddOrganizationAsync(Organization organization);
    Task<Organization?> UpdateOrganizationAsync(Organization organization);
    Task<bool> DeleteOrganizationAsync(int organizationId);
    Task<CategorySuggestion?> AddCategorySuggestionAsync(CategorySuggestion suggestion);
    Task<CategorySuggestionReviewResult> ReviewCategorySuggestionAsync(int suggestionId, CategorySuggestionStatus status);
    Task<Problem?> FindProblemAsync(int id);
    Task<Proposal?> FindProposalAsync(int id);
    Task<Solution?> FindSolutionAsync(int id);
    Task<User?> FindUserAsync(int id);
    Task<Problem?> AddProblemAsync(Problem problem);
    Task<Problem?> UpdateProblemAsync(Problem problem);
    Task<Proposal> AddProposalAsync(Proposal proposal);
    Task<Solution> AddSolutionAsync(Solution solution);
    Task<Evaluation> AddEvaluationAsync(Evaluation evaluation);
    Task<Proposal?> UpdateProposalStatusAsync(int proposalId, ProposalStatus status);
    Task<Problem?> UpdateProblemStatusAsync(int problemId, ProblemStatus status);
}
