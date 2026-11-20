using System.Text;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

internal static class GovernedAnswerPostProcessor
{
    private const string StandardLowConfidenceAnswer = "I do not have enough governed evidence to answer that safely. Please name the source domain, runbook, repo area, log, or CWI component you want me to use, and I can retry with governed retrieval.";

    private static readonly string[] InternalScaffoldingPrefixes =
    {
        "Query class:",
        "Intent type:",
        "Intent mode:",
        "Audience mode:",
        "Verbosity mode:",
        "Tone mode:",
        "Response template:",
        "Template used:",
        "Summary mode:",
        "Code mode:",
        "Clarification mode:",
        "Evidence required:",
        "Grounding mode:",
        "Retrieval note:",
        "Retrieval skipped reason:",
        "Confidence basis:",
        "Internal routing context:",
        "Reasoning receipt:",
        "Hidden instruction:"
    };

    private static readonly string[] InternalScaffoldingFragments =
    {
        "intent identification",
        "intent classification",
        "identified the intent",
        "query classification",
        "evidence review",
        "evidence analysis",
        "template analysis",
        "selected template",
        "response template",
        "grounding analysis",
        "confidence basis",
        "internal reasoning",
        "internal analysis",
        "reasoning steps",
        "response generation",
        "response-generation",
        "internal response-generation",
        "scaffolding"
    };

    public static string ApplyResponseOrchestration(string answer)
    {
        return Normalize(answer);
    }

    public static string ApplyResponseOrchestration(string answer, AskWorkflowContext context)
    {
        var normalized = Normalize(answer);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var shaped = context.TemplateUsed switch
        {
            "operator_troubleshooting" => ApplyTroubleshootingTemplate(normalized, context),
            "operator_steps" => ApplyOperatorStepsTemplate(normalized),
            "executive_summary" => ApplyExecutiveTemplate(normalized),
            "risk_summary" => ApplyRiskTemplate(normalized),
            "daily_briefing" => ApplyDailyBriefingTemplate(normalized),
            "change_summary" => ApplyChangeTemplate(normalized),
            "plan_summary" => ApplyPlanTemplate(normalized),
            "low_confidence_escalation" => ApplyLowConfidenceTemplate(normalized),
            "memory_acknowledgement" => ApplyMemoryAcknowledgementTemplate(normalized, context),
            "summary_answer" => ApplyDirectAnswerTemplate(normalized),
            "code_answer" => ApplyDirectAnswerTemplate(normalized),
            "tool_readiness" => ApplyDirectAnswerTemplate(normalized),
            "clarification" => ApplyDirectAnswerTemplate(normalized),
            "direct_answer" => ApplyDirectAnswerTemplate(normalized),
            "system_truth_answer" => ApplySystemTruthTemplate(normalized),
            "general_answer" => ApplyDirectAnswerTemplate(normalized),
            _ => normalized
        };

        return ApplyFinalBehaviorConstraints(shaped, context);
    }

    public static string ApplyCommunicationCore(string answer)
    {
        return Normalize(answer);
    }

    public static string ApplyCommunicationCore(string answer, AskWorkflowContext context)
    {
        var normalized = Normalize(answer);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return ApplyFinalBehaviorConstraints(normalized, context);
    }

    private static string ApplyTroubleshootingTemplate(string answer, AskWorkflowContext context)
    {
        var hasNumberedSteps = answer.Contains("1.", StringComparison.Ordinal)
            || answer.Contains("1)", StringComparison.Ordinal);
        var hasVerification = answer.Contains("Verification command", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("```bash", StringComparison.OrdinalIgnoreCase);

        if (hasNumberedSteps && hasVerification)
        {
            return answer;
        }

        var builder = new StringBuilder();
        if (!hasNumberedSteps)
        {
            builder.AppendLine("Start here:");
            builder.AppendLine();
            builder.AppendLine($"1. {TrimTrailingPeriod(answer)}.");
            builder.AppendLine("2. Check the latest logs around the exact failure time.");
            builder.AppendLine("3. Verify the service endpoint from the same runtime or network path that failed.");
        }
        else
        {
            builder.AppendLine(answer);
        }

        if (!hasVerification)
        {
            builder.AppendLine();
            builder.AppendLine("Verification command:");
            builder.AppendLine("```bash");
            if (MentionsAny(context, "docker", "container", "curl"))
            {
                builder.AppendLine("docker ps --format \"{{.Names}} {{.Status}} {{.Ports}}\"");
                builder.AppendLine("docker logs --tail 100 <container>");
                builder.AppendLine("curl -v <health-url>");
            }
            else
            {
                builder.AppendLine("echo \"replace with the service-specific health check\"");
            }
            builder.AppendLine("```");
        }

        return Normalize(builder.ToString());
    }

    private static string ApplyExecutiveTemplate(string answer)
    {
        if (answer.Contains("Impact:", StringComparison.OrdinalIgnoreCase)
            && answer.Contains("Recommendation:", StringComparison.OrdinalIgnoreCase))
        {
            return answer;
        }

        return Normalize($"""
            Impact:
            {answer}

            Recommendation:
            Use this as the current decision frame, then validate the named source or operating context before making production changes.
            """);
    }

    private static string ApplyOperatorStepsTemplate(string answer)
    {
        if (answer.Contains("1.", StringComparison.Ordinal)
            || answer.Contains("1)", StringComparison.Ordinal))
        {
            return answer;
        }

        return Normalize($"""
            Steps:

            1. {TrimTrailingPeriod(answer)}.
            """);
    }

    private static string ApplyRiskTemplate(string answer)
    {
        if (HasSection(answer, "risks", "risk")
            && HasSection(answer, "impact")
            && HasSection(answer, "next action", "next step"))
        {
            return answer;
        }

        var risk = FirstMeaningfulLine(answer);
        return Normalize($"""
            Risks:
            1. {TrimTrailingPeriod(risk)}.

            Impact:
            This is the item most likely to affect timing, quality, access, cost, or user trust if it is not handled.

            Next action:
            Confirm owner, deadline, and the latest source evidence before taking action.
            """);
    }

    private static string ApplyDailyBriefingTemplate(string answer)
    {
        if (HasSection(answer, "priorities", "priority")
            && HasSection(answer, "risk", "risks")
            && HasSection(answer, "recommendation", "recommendations"))
        {
            return answer;
        }

        var priority = FirstMeaningfulLine(answer);
        return Normalize($"""
            Priorities:
            1. {TrimTrailingPeriod(priority)}.

            Risk:
            1. Verify that the briefing is based on current memory, graph, calendar, task, or connector data.

            Recommendation:
            Handle the highest-confidence priority first, then refresh the briefing after the next sync.
            """);
    }

    private static string ApplyChangeTemplate(string answer)
    {
        if (HasSection(answer, "changes", "change")
            && HasSection(answer, "risk", "risks")
            && HasSection(answer, "suggestion", "suggestions"))
        {
            return answer;
        }

        var change = FirstMeaningfulLine(answer);
        return Normalize($"""
            Changes:
            1. {TrimTrailingPeriod(change)}.

            Risks:
            1. Verify that the change review is based on current memory, graph, calendar, task, or connector data.

            Suggestions:
            1. Refresh the relevant data, then rerun the change review.
            """);
    }

    private static string ApplyPlanTemplate(string answer)
    {
        if (HasSection(answer, "priorities", "priority")
            && HasSection(answer, "risk", "risks")
            && HasSection(answer, "suggestion", "suggestions"))
        {
            return answer;
        }

        var priority = FirstMeaningfulLine(answer);
        return Normalize($"""
            Priorities:
            1. {TrimTrailingPeriod(priority)}.

            Risks:
            1. Verify that the plan is based on current memory, graph, calendar, task, or connector data.

            Suggestions:
            1. Confirm owner and deadline for the first priority.
            """);
    }

    private static string ApplyLowConfidenceTemplate(string answer)
    {
        return StandardLowConfidenceAnswer;
    }

    private static string ApplyMemoryAcknowledgementTemplate(string answer, AskWorkflowContext context)
    {
        if (string.Equals(context.QueryClassification, "self_context_update", StringComparison.OrdinalIgnoreCase))
        {
            return answer;
        }

        if (answer.StartsWith("Preference saved.", StringComparison.OrdinalIgnoreCase)
            || answer.StartsWith("Memory updated.", StringComparison.OrdinalIgnoreCase))
        {
            return answer;
        }

        return Normalize($"Preference saved. {answer}");
    }

    private static string ApplyDirectAnswerTemplate(string answer)
    {
        return answer;
    }

    private static string ApplySystemTruthTemplate(string answer)
    {
        return answer;
    }

    private static string ApplyFinalBehaviorConstraints(string answer, AskWorkflowContext context)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var priorityItemLimit = GovernedResponseModes.IsPlanMode(context) ? 5 : 3;
        var shaped = LimitSectionItems(answer, new[] { "priorities", "priority" }, priorityItemLimit);
        var riskItemLimit = GovernedResponseModes.IsRiskMode(context) ? 2 : 1;
        shaped = LimitSectionItems(shaped, new[] { "risks", "risk" }, riskItemLimit);
        shaped = LimitSectionItems(shaped, new[] { "changes", "change" }, 5);
        shaped = LimitSectionItems(shaped, new[] { "next steps", "next step", "next action" }, 1);
        shaped = LimitSectionItems(shaped, new[] { "recommendations", "recommendation" }, 1);
        shaped = LimitSectionItems(shaped, new[] { "suggestions", "suggestion" }, 3);

        if (!PolicyLanguageRequested(context))
        {
            shaped = StripUnrequestedPolicyLanguage(shaped);
        }

        return Normalize(shaped);
    }

    private static string LimitSectionItems(string answer, IReadOnlyCollection<string> sectionNames, int maxItems)
    {
        var builder = new StringBuilder(answer.Length);
        var activeSection = false;
        var itemCount = 0;

        foreach (var rawLine in answer.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (TryReadSectionTitle(line, out var title))
            {
                activeSection = sectionNames.Any(section =>
                    title.Equals(section, StringComparison.OrdinalIgnoreCase)
                    || title.Contains(section, StringComparison.OrdinalIgnoreCase));
                itemCount = 0;
                AppendLine(builder, line);
                continue;
            }

            if (activeSection && IsListItem(line))
            {
                itemCount++;
                if (itemCount > maxItems)
                {
                    continue;
                }
            }

            AppendLine(builder, line);
        }

        return builder.ToString().Trim();
    }

    private static bool TryReadSectionTitle(string line, out string title)
    {
        title = string.Empty;
        var trimmed = line.Trim().Trim('*', '#', ' ');
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.EndsWith(':'))
        {
            title = trimmed.TrimEnd(':').Trim();
            return title.Length > 0 && title.Length <= 60;
        }

        if (!line.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        title = trimmed;
        return title.Length > 0 && title.Length <= 60;
    }

    private static bool IsListItem(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("- ", StringComparison.Ordinal)
               || trimmed.StartsWith("* ", StringComparison.Ordinal)
               || Regex.IsMatch(trimmed, @"^\d+[\.\)]\s+", RegexOptions.CultureInvariant);
    }

    private static bool PolicyLanguageRequested(AskWorkflowContext context)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        return primaryQuestion.Contains("policy", StringComparison.OrdinalIgnoreCase)
               || primaryQuestion.Contains("framework", StringComparison.OrdinalIgnoreCase)
               || primaryQuestion.Contains("governance", StringComparison.OrdinalIgnoreCase)
               || primaryQuestion.Contains("compliance", StringComparison.OrdinalIgnoreCase)
               || primaryQuestion.Contains("regulation", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripUnrequestedPolicyLanguage(string answer)
    {
        var builder = new StringBuilder(answer.Length);
        foreach (var rawLine in answer.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (ShouldStripPolicyLine(line))
            {
                continue;
            }

            AppendLine(builder, line);
        }

        return builder.ToString().Trim();
    }

    private static void AppendLine(StringBuilder builder, string line)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(line);
    }

    private static bool HasSection(string answer, params string[] names)
    {
        foreach (var line in answer.Split('\n'))
        {
            if (!TryReadSectionTitle(line, out var title))
            {
                continue;
            }

            if (names.Any(name => title.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || title.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string FirstMeaningfulLine(string answer)
    {
        foreach (var line in answer.Split('\n'))
        {
            var trimmed = RemoveLeadingListMarker(line.Trim());
            if (string.IsNullOrWhiteSpace(trimmed)
                || TryReadSectionTitle(trimmed, out _)
                || ShouldStripPolicyLine(trimmed))
            {
                continue;
            }

            return trimmed;
        }

        return "No specific current item was identified";
    }

    private static bool ShouldStripPolicyLine(string line)
    {
        return line.Contains("policy/framework", StringComparison.OrdinalIgnoreCase)
               || line.Contains("policy framework", StringComparison.OrdinalIgnoreCase)
               || line.Contains("governance framework", StringComparison.OrdinalIgnoreCase)
               || line.Contains("enterprise framework", StringComparison.OrdinalIgnoreCase)
               || line.Contains("enterprise policy", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingPeriod(string value)
    {
        return value.Trim().TrimEnd('.');
    }

    private static bool MentionsAny(AskWorkflowContext context, params string[] terms)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        return terms.Any(term =>
            primaryQuestion.Contains(term, StringComparison.OrdinalIgnoreCase)
            || context.RetrievalDomainTerms.Any(domainTerm => string.Equals(domainTerm, term, StringComparison.OrdinalIgnoreCase)));
    }

    private static string Normalize(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var normalized = answer
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = StripInternalScaffolding(normalized);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        var blankLineCount = 0;
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank)
            {
                blankLineCount++;
                if (blankLineCount > 1)
                {
                    continue;
                }
            }
            else
            {
                blankLineCount = 0;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(isBlank ? string.Empty : line);
        }

        return builder.ToString().Trim();
    }

    private static string StripInternalScaffolding(string answer)
    {
        var builder = new StringBuilder(answer.Length);
        foreach (var rawLine in answer.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();
            var unmarked = RemoveLeadingListMarker(trimmed);
            if (InternalScaffoldingPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || unmarked.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || InternalScaffoldingFragments.Any(fragment => trimmed.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line);
        }

        return builder.ToString().Trim();
    }

    private static string RemoveLeadingListMarker(string value)
    {
        var trimmed = value.TrimStart('-', '*', ' ');
        var markerEnd = trimmed.IndexOfAny(new[] { '.', ')' });
        if (markerEnd <= 0 || markerEnd > 3)
        {
            return trimmed;
        }

        return trimmed[..markerEnd].All(char.IsDigit)
            ? trimmed[(markerEnd + 1)..].TrimStart()
            : trimmed;
    }
}
