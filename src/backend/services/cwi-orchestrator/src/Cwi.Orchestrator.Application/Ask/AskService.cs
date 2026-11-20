using System.Diagnostics;
using Cwi.Common;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Ask;

public sealed class AskService : DomainServiceBase
{
    private readonly AskWorkflowPipeline _pipeline;
    private readonly ApplicationOptions _options;
    private readonly JourneyRequestContext _journeyContext;
    private readonly ILanguageNormalizationClient _normalizationClient;

    public AskService(
        AskWorkflowPipeline pipeline,
        ApplicationOptions options,
        JourneyRequestContext journeyContext,
        ILanguageNormalizationClient? normalizationClient = null)
    {
        _pipeline = pipeline;
        _options = options;
        _journeyContext = journeyContext;
        _normalizationClient = normalizationClient ?? new LocalLanguageNormalizationClient();
    }

    public async Task<ServiceResponse<AskResponseDto>> HandleAsync(
        string tenantId,
        string userId,
        string question,
        string packName,
        string? requestedPackAnchor,
        string policyMode,
        int topK,
        string? modelTier,
        string? conversationId,
        string? effectiveTimezone,
        CancellationToken cancellationToken,
        AskWebRetrievalContext? webRetrieval = null,
        bool useWebRequested = false)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(question);
        var normalization = await _normalizationClient
            .NormalizeAsync(primaryQuestion, cancellationToken)
            .ConfigureAwait(false);
        var routingQuestion = string.IsNullOrWhiteSpace(normalization.NormalizedPrompt)
            ? question
            : normalization.NormalizedPrompt;
        var domainPackName = DomainPackResolver.Resolve(routingQuestion, packName, requestedPackAnchor);
        var context = new AskWorkflowContext(
            tenantId,
            userId,
            question,
            domainPackName,
            requestedPackAnchor,
            policyMode,
            topK,
            conversationId,
            effectiveTimezone,
            useWebRequested || webRetrieval?.Enabled == true,
            normalization);
        context.DomainPackNames = new[] { domainPackName };
        context.ModelTier = modelTier;
        context.PolicyVersion = _options.PolicyVersion;
        context.RequestId = _journeyContext.RequestId;
        context.TrustedWebContextRequested = webRetrieval?.Enabled == true;
        context.TrustedWebContextMode = webRetrieval?.Mode;
        context.TrustedWebContextProfile = webRetrieval?.Profile;
        context.TrustedWebContextMaxResults = webRetrieval?.MaxResults;
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            context.TraceId = traceId;
        }
        else if (!string.IsNullOrWhiteSpace(_journeyContext.TraceId))
        {
            context.TraceId = _journeyContext.TraceId;
        }

        return await ExecuteAsync(() =>
        {
            return _pipeline.ExecuteAsync(context, cancellationToken);
        }).ConfigureAwait(false);
    }
}
