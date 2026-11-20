#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class CandidateAwareGraphEvidenceProvider : IGraphEvidenceProvider
{
    private static readonly Regex TokenRegex = new(
        "[A-Za-z0-9][A-Za-z0-9_-]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "also", "and", "are", "ask", "based", "can", "contract", "does",
        "for", "from", "governed", "has", "have", "how", "into", "own", "owns", "role",
        "source", "structural", "the", "this", "using", "validation", "what", "when",
        "where", "which", "who", "why", "with"
    };

    private readonly IGraphEvidenceProvider _primaryProvider;
    private readonly IGraphUpdateCandidateRepository _candidateRepository;

    public CandidateAwareGraphEvidenceProvider(
        IGraphEvidenceProvider primaryProvider,
        IGraphUpdateCandidateRepository candidateRepository)
    {
        _primaryProvider = primaryProvider;
        _candidateRepository = candidateRepository;
    }

    public async Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(
        GraphEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        var primary = await _primaryProvider.RetrieveAsync(request, cancellationToken).ConfigureAwait(false);
        var candidateEvidence = await RetrieveCandidateEvidenceAsync(request, cancellationToken).ConfigureAwait(false);

        if ((!primary.IsSuccess || primary.Data is null) && candidateEvidence.Count == 0)
        {
            return primary;
        }

        var primaryData = primary.Data ?? new GraphQueryResult();
        var combined = primaryData.Evidence
            .Concat(candidateEvidence)
            .DistinctBy(evidence => evidence.SourceToken, StringComparer.OrdinalIgnoreCase)
            .Take(request.TopK.GetValueOrDefault(5))
            .ToArray();

        return ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult
        {
            Template = primaryData.Template ?? (candidateEvidence.Count > 0 ? "graph_update_candidate" : null),
            Terms = primaryData.Terms
                .Concat(ExtractTerms(request.Question))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray(),
            Evidence = combined
        });
    }

    private async Task<IReadOnlyList<GraphEvidence>> RetrieveCandidateEvidenceAsync(
        GraphEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        var terms = ExtractTerms(request.Question);
        if (terms.Count == 0)
        {
            return Array.Empty<GraphEvidence>();
        }

        var candidates = await _candidateRepository.QueryAsync(
            string.IsNullOrWhiteSpace(request.TenantId) ? null : request.TenantId.Trim(),
            null,
            "current",
            0,
            Math.Min(Math.Max(request.TopK.GetValueOrDefault(5) * 20, 50), 200),
            cancellationToken).ConfigureAwait(false);

        return candidates
            .Where(candidate => !string.Equals(candidate.CandidateState, "conflict", StringComparison.OrdinalIgnoreCase))
            .Where(candidate => Matches(candidate, terms))
            .Select(ToEvidence)
            .Take(request.TopK.GetValueOrDefault(5))
            .ToArray();
    }

    private static GraphEvidence ToEvidence(GraphUpdateCandidateRecord candidate)
    {
        return new GraphEvidence
        {
            Subject = candidate.Subject,
            Relation = candidate.Relation,
            Object = candidate.Object,
            Template = "graph_update_candidate",
            SourceDocument = candidate.CandidateId,
            SourcePackTarget = "recursive_graph_candidate",
            TargetPackTarget = "graph_vector_refresh",
            Inferred = false
        };
    }

    private static bool Matches(GraphUpdateCandidateRecord candidate, IReadOnlyList<string> terms)
    {
        var haystack = new StringBuilder()
            .Append(candidate.Subject).Append(' ')
            .Append(candidate.Relation).Append(' ')
            .Append(candidate.Object).Append(' ')
            .Append(candidate.FactKey).Append(' ')
            .Append(candidate.SourceComponent).Append(' ')
            .Append(candidate.PayloadJson)
            .ToString();

        var requiredMatches = terms.Count >= 3 ? 2 : 1;
        return terms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) >= requiredMatches;
    }

    private static IReadOnlyList<string> ExtractTerms(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Array.Empty<string>();
        }

        return TokenRegex.Matches(question)
            .Select(match => match.Value.Trim().ToLowerInvariant())
            .Where(term => !StopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }
}
#nullable disable
