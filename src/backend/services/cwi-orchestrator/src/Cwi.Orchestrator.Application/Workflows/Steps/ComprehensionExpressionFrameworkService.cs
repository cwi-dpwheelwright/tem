#nullable enable
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class ComprehensionExpressionFrameworkService
{
    public const string Version = "comprehension-expression-framework-v1";

    public ComprehensionExpressionDecision Evaluate(AskWorkflowContext context)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        var contextMode = ResolveContextMode(context, primaryQuestion);
        var clarityDecision = ResolveClarityDecision(context, contextMode);
        var tone = ResolveTone(context, primaryQuestion, contextMode);

        return new ComprehensionExpressionDecision(
            FrameworkVersion: Version,
            ContextMode: contextMode,
            ClarityDecision: clarityDecision.Decision,
            ClarityReason: clarityDecision.Reason,
            ToneProfile: tone.Profile,
            ToneReason: tone.Reason,
            AudienceMode: tone.AudienceMode,
            VerbosityMode: tone.VerbosityMode);
    }

    private static string ResolveContextMode(AskWorkflowContext context, string question)
    {
        if (string.Equals(context.IntentType, "needs_clarification", StringComparison.OrdinalIgnoreCase))
        {
            return "clarification";
        }

        if (IsSensitive(question) || context.GuardrailViolations.Count > 0)
        {
            return "sensitive";
        }

        if (GovernedResponseModes.TryResolveStructuredMode(context, out _, out _))
        {
            return "structured_report";
        }

        if (IsToolIntent(context.IntentType))
        {
            return "tool";
        }

        if (string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase))
        {
            return "memory";
        }

        if (string.Equals(context.IntentType, "code_generation", StringComparison.OrdinalIgnoreCase))
        {
            return "technical";
        }

        if (string.Equals(context.IntentType, "summarization", StringComparison.OrdinalIgnoreCase))
        {
            return "summarization";
        }

        if (string.Equals(context.IntentType, "message_draft", StringComparison.OrdinalIgnoreCase))
        {
            return "communication_draft";
        }

        if (string.Equals(context.IntentType, "food_safety", StringComparison.OrdinalIgnoreCase))
        {
            return "sensitive";
        }

        if (string.Equals(context.IntentType, "practical_guidance", StringComparison.OrdinalIgnoreCase))
        {
            return "practical_guidance";
        }

        if (string.Equals(context.AudienceMode, "executive", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(question, "email update", "ceo", "executive", "approval"))
        {
            return "executive";
        }

        return "direct_answer";
    }

    private static ClarityDecision ResolveClarityDecision(AskWorkflowContext context, string contextMode)
    {
        if (string.Equals(context.IntentType, "needs_clarification", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "clarification", StringComparison.OrdinalIgnoreCase))
        {
            return new ClarityDecision("clarity_clarification_required", "intent_or_entity_requires_user_clarification");
        }

        if (context.EvidenceRequired && !context.RetrievalAllowed)
        {
            return new ClarityDecision("clarity_warning_preserved", "evidence_required_without_retrieval");
        }

        if (context.MemoryConflictSummaries.Count > 0)
        {
            return new ClarityDecision("clarity_ambiguity_preserved", "memory_conflict_must_remain_visible");
        }

        if (string.Equals(contextMode, "structured_report", StringComparison.OrdinalIgnoreCase)
            || GovernedResponseModes.TryResolveStructuredMode(context, out _, out _))
        {
            return new ClarityDecision("clarity_mode_shape_enforced", "response_mode_template_controls_shape");
        }

        if (string.Equals(contextMode, "direct_answer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contextMode, "practical_guidance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contextMode, "summarization", StringComparison.OrdinalIgnoreCase))
        {
            return new ClarityDecision("clarity_answer_first", "answer_should_lead_without_policy_boilerplate");
        }

        return new ClarityDecision("clarity_no_change_needed", "existing_response_mode_is_clear");
    }

    private static ToneDecision ResolveTone(AskWorkflowContext context, string question, string contextMode)
    {
        if (IsSensitive(question) || context.GuardrailViolations.Count > 0)
        {
            return new ToneDecision("tone_sensitive", "tone_safety_override", null, "concise");
        }

        if (ContainsAny(question, "executive", "ceo", "approval", "email update", "board update"))
        {
            return new ToneDecision("tone_executive", "tone_explicit_user_request", "executive", "concise");
        }

        if (ContainsAny(question, "plain english", "simple words", "short words", "explain simply"))
        {
            return new ToneDecision("tone_plain_language", "tone_explicit_user_request", null, "concise");
        }

        if (string.Equals(context.IntentType, "message_draft", StringComparison.OrdinalIgnoreCase))
        {
            return new ToneDecision("tone_respectful_concise", "tone_message_draft_context", null, "concise");
        }

        if (ContainsAny(question, "technical", "engineering", "implementation", "debug", "code"))
        {
            return new ToneDecision("tone_technical", "tone_explicit_user_request", null, "step_by_step");
        }

        if (ContainsAny(question, "urgent", "asap", "right now", "immediately"))
        {
            return new ToneDecision("tone_urgent", "tone_explicit_user_request", null, "concise");
        }

        return contextMode switch
        {
            "technical" => new ToneDecision("tone_technical", "tone_context_fit", null, "step_by_step"),
            "executive" => new ToneDecision("tone_executive", "tone_context_fit", "executive", "concise"),
            "memory" => new ToneDecision("tone_default_helpful", "tone_memory_context", null, "concise"),
            "clarification" => new ToneDecision("tone_default_helpful", "tone_clarification_context", null, "concise"),
            "practical_guidance" => new ToneDecision("tone_practical_direct", "tone_everyday_guidance_context", null, "concise"),
            _ => new ToneDecision("tone_default_helpful", "tone_default_helpful", null, null)
        };
    }

    private static bool IsToolIntent(string? intentType)
    {
        return !string.IsNullOrWhiteSpace(intentType)
               && intentType.Contains('.', StringComparison.Ordinal)
               && !string.Equals(intentType, "current_date_intent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSensitive(string value)
    {
        return ContainsAny(
            value,
            "password",
            "api key",
            "token",
            "private key",
            "secret",
            "medical",
            "health",
            "legal",
            "lawsuit",
            "financial",
            "emergency",
            "fire",
            "gas leak",
            "electrical",
            "injury",
            "danger");
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ClarityDecision(string Decision, string Reason);

    private sealed record ToneDecision(
        string Profile,
        string Reason,
        string? AudienceMode,
        string? VerbosityMode);
}

public sealed record ComprehensionExpressionDecision(
    string FrameworkVersion,
    string ContextMode,
    string ClarityDecision,
    string ClarityReason,
    string ToneProfile,
    string ToneReason,
    string? AudienceMode,
    string? VerbosityMode);
#nullable disable
