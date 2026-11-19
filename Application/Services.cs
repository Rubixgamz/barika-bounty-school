using School_Project.Data;
using School_Project.Models;

namespace School_Project.Application;

public sealed class ChallengeService(IAppRepository repository)
{
    public async Task<IReadOnlyList<Problem>> SearchProblemsAsync(string? search, int? categoryId, ProblemStatus? status)
    {
        var problems = await repository.GetProblemsAsync();
        var query = problems.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(problem =>
                problem.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                problem.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (categoryId.HasValue) query = query.Where(problem => problem.CategoryId == categoryId.Value);
        if (status.HasValue) query = query.Where(problem => problem.Status == status.Value);

        return query.OrderBy(problem => problem.Deadline).ToList();
    }

    public Task<Problem?> CreateProblemAsync(CreateProblemRequest request)
    {
        var problem = new Problem(0, request.Title, request.Description, request.OrganizationId, request.CategoryId, ProblemStatus.Open, request.Deadline, request.RequiredSkillIds, request.BountyAmount, request.OffersEmployment);
        return repository.AddProblemAsync(problem);
    }
}

public sealed class ProposalService(IAppRepository repository)
{
    public async Task<IReadOnlyList<object>> SearchProposalsAsync(
        string? search,
        ProposalStatus? status,
        int? userId = null,
        int? organizationId = null)
    {
        var proposals = await repository.GetProposalsAsync();
        var problems = await repository.GetProblemsAsync();
        var categories = await repository.GetCategoriesAsync();
        var users = await repository.GetUsersAsync();
        var query = proposals.AsEnumerable();

        if (userId.HasValue) query = query.Where(proposal => proposal.UserId == userId.Value);
        if (organizationId.HasValue)
        {
            var problemIds = problems
                .Where(problem => problem.OrganizationId == organizationId.Value)
                .Select(problem => problem.Id)
                .ToHashSet();
            query = query.Where(proposal => problemIds.Contains(proposal.ProblemId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(proposal =>
                proposal.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                proposal.Summary.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (status.HasValue) query = query.Where(proposal => proposal.Status == status.Value);

        return query
            .OrderByDescending(proposal => proposal.SubmittedAt)
            .Select(proposal =>
            {
                var problem = problems.FirstOrDefault(problem => problem.Id == proposal.ProblemId);
                return new
                {
                    Proposal = proposal,
                    Problem = problem,
                    Category = problem is null ? null : categories.FirstOrDefault(category => category.Id == problem.CategoryId),
                    User = users.FirstOrDefault(user => user.Id == proposal.UserId) is { } user ? new ProposalAuthor(user.Id, user.FullName) : null
                };
            })
            .Cast<object>()
            .ToList();
    }

    public async Task<Proposal?> SubmitProposalAsync(CreateProposalRequest request)
    {
        var problem = await repository.FindProblemAsync(request.ProblemId);
        var user = await repository.FindUserAsync(request.UserId);
        if (problem is null || problem.Status != ProblemStatus.Open || user?.Role != UserRole.Innovator) return null;

        var proposal = new Proposal(0, request.ProblemId, request.UserId, request.Title, request.Summary, ProposalStatus.Submitted, DateTime.UtcNow);
        return await repository.AddProposalAsync(proposal);
    }

    public async Task<Solution?> SubmitSolutionAsync(int proposalId, int userId, SubmitSolutionRequest request)
    {
        var proposal = await repository.FindProposalAsync(proposalId);
        if (proposal is null || proposal.UserId != userId) return null;

        await repository.UpdateProposalStatusAsync(proposalId, ProposalStatus.Approved);
        return await repository.AddSolutionAsync(new Solution(0, proposalId, request.ImplementationPlan, request.EvidenceUrl, DateTime.UtcNow));
    }
}

public sealed class EvaluationService(IAppRepository repository)
{
    public async Task<Evaluation?> EvaluateAsync(CreateEvaluationRequest request)
    {
        var solution = await repository.FindSolutionAsync(request.SolutionId);
        var evaluator = await repository.FindUserAsync(request.EvaluatorUserId);

        if (solution is null || evaluator is null) return null;
        if (evaluator.Role is not (UserRole.Evaluator or UserRole.Admin)) return null;

        var score = Math.Clamp(request.Score, 0, 100);
        return await repository.AddEvaluationAsync(new Evaluation(0, request.SolutionId, request.EvaluatorUserId, score, request.Comments, DateTime.UtcNow));
    }
}

public sealed class DashboardService(IAppRepository repository)
{
    public async Task<object> GetSummaryAsync(int? organizationId = null)
    {
        var users = await repository.GetUsersAsync();
        var organizations = await repository.GetOrganizationsAsync();
        var allProblems = await repository.GetProblemsAsync();
        var problems = organizationId.HasValue
            ? allProblems.Where(problem => problem.OrganizationId == organizationId.Value).ToList()
            : allProblems.ToList();
        var problemIds = problems.Select(problem => problem.Id).ToHashSet();
        var proposals = (await repository.GetProposalsAsync())
            .Where(proposal => problemIds.Contains(proposal.ProblemId))
            .ToList();
        var proposalIds = proposals.Select(proposal => proposal.Id).ToHashSet();
        var solutions = (await repository.GetSolutionsAsync())
            .Where(solution => proposalIds.Contains(solution.ProposalId))
            .ToList();
        var solutionIds = solutions.Select(solution => solution.Id).ToHashSet();
        var evaluations = (await repository.GetEvaluationsAsync())
            .Where(evaluation => solutionIds.Contains(evaluation.SolutionId))
            .ToList();
        var records = (await repository.GetImplementationRecordsAsync())
            .Where(record => solutionIds.Contains(record.SolutionId))
            .ToList();
        var scopedUsers = organizationId.HasValue
            ? users.Where(user => user.OrganizationId == organizationId.Value).ToList()
            : users;
        var scopedOrganizations = organizationId.HasValue
            ? organizations.Where(organization => organization.Id == organizationId.Value).ToList()
            : organizations;

        return new
        {
            totals = new
            {
                users = scopedUsers.Count,
                organizations = scopedOrganizations.Count,
                problems = problems.Count,
                proposals = proposals.Count,
                solutions = solutions.Count,
                evaluations = evaluations.Count,
                implementationRecords = records.Count
            },
            problemsByStatus = problems.GroupBy(problem => problem.Status.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            proposalsByStatus = proposals.GroupBy(proposal => proposal.Status.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            averageEvaluationScore = evaluations.Count == 0 ? 0 : Math.Round(evaluations.Average(evaluation => evaluation.Score), 1),
            latestImplementation = records.OrderByDescending(record => record.UpdatedAt).FirstOrDefault()
        };
    }
}
