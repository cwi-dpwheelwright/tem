#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class GraphQueryService
{
    private readonly IGraphEvidenceProvider _graphEvidenceProvider;

    public GraphQueryService(IGraphEvidenceProvider graphEvidenceProvider)
    {
        _graphEvidenceProvider = graphEvidenceProvider;
    }

    public async Task<ServiceResponse<GraphQueryResponseDto>> QueryAsync(
        GraphQueryRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return ServiceResponse<GraphQueryResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id is required");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return ServiceResponse<GraphQueryResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "question is required."),
                statusCode: 400,
                message: "question is required");
        }

        var result = await _graphEvidenceProvider
            .RetrieveAsync(new GraphEvidenceRequest(
                request.Question.Trim(),
                request.TenantId.Trim(),
                string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId.Trim(),
                request.TopK), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return ServiceResponse<GraphQueryResponseDto>.Fail(
                result.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Graph query failed."),
                statusCode: result.StatusCode,
                message: result.Message);
        }

        return ServiceResponse<GraphQueryResponseDto>.Success(new GraphQueryResponseDto
        {
            Template = result.Data.Template,
            Terms = result.Data.Terms,
            Evidence = result.Data.Evidence.Select(ToDto).ToArray()
        });
    }

    public static GraphEvidenceDto ToDto(GraphEvidence evidence)
    {
        return new GraphEvidenceDto
        {
            Subject = evidence.Subject,
            Relation = evidence.Relation,
            Object = evidence.Object,
            Template = evidence.Template,
            SourceDocument = evidence.SourceDocument,
            SourcePackTarget = evidence.SourcePackTarget,
            TargetPackTarget = evidence.TargetPackTarget,
            Inferred = evidence.Inferred
        };
    }
}

public sealed class UserGraphProfileService
{
    private static readonly (string Name, string Question)[] ProfileQueries =
    {
        ("current_work", "What have I been working on?"),
        ("open_loops", "What are my open loops?"),
        ("goals", "What are my goals?"),
        ("home_life", "What do you know about my home life?"),
        ("work_life", "What do you know about my work life?"),
        ("decisions", "What decisions have I made?")
    };

    private readonly IGraphEvidenceProvider _graphEvidenceProvider;

    public UserGraphProfileService(IGraphEvidenceProvider graphEvidenceProvider)
    {
        _graphEvidenceProvider = graphEvidenceProvider;
    }

    public async Task<ServiceResponse<UserGraphProfileResponseDto>> GetAsync(
        string tenantId,
        string? userId,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<UserGraphProfileResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id is required");
        }

        var sections = new List<UserGraphProfileSectionDto>();
        foreach (var (name, question) in ProfileQueries)
        {
            var result = await _graphEvidenceProvider
                .RetrieveAsync(new GraphEvidenceRequest(question, tenantId.Trim(), userId, topK), cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.Data is null)
            {
                continue;
            }

            sections.Add(new UserGraphProfileSectionDto
            {
                Name = name,
                Evidence = result.Data.Evidence.Select(GraphQueryService.ToDto).ToArray()
            });
        }

        return ServiceResponse<UserGraphProfileResponseDto>.Success(new UserGraphProfileResponseDto
        {
            TenantId = tenantId.Trim(),
            UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
            Sections = sections
        });
    }
}
#nullable disable
