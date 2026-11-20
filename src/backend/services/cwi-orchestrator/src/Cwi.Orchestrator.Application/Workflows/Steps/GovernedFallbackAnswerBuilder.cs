#nullable enable
using Cwi.Orchestrator.Application.Ask;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

internal static class GovernedFallbackAnswerBuilder
{
    public static string BuildEvidenceUnavailable(AskWorkflowContext context)
    {
        if (string.Equals(context.TemplateUsed, "risk_summary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "risk_review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "risk_analysis", StringComparison.OrdinalIgnoreCase))
        {
            return """
                Risks:
                1. I do not have enough current task, calendar, note, or project details to name a specific risk safely.

                Impact:
                Treat this as unknown, not clear.

                Next action:
                Add or connect the relevant task, calendar, note, or project source, then ask again.
                """;
        }

        if (string.Equals(context.TemplateUsed, "daily_briefing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "daily_briefing", StringComparison.OrdinalIgnoreCase))
        {
            return """
                Priorities:
                1. I do not have current calendar, task, or reminder details to rank today's priorities safely.

                Risk:
                1. The main risk is missing or stale context.

                Recommendation:
                Add or connect the relevant calendar, task, or note source, then ask for the daily briefing again.
                """;
        }

        if (string.Equals(context.TemplateUsed, "change_summary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "change_review", StringComparison.OrdinalIgnoreCase))
        {
            return """
                Changes:
                1. I do not have recent change details available.

                Risks:
                1. The main risk is missing or stale context.

                Suggestions:
                1. Add or connect the relevant task, calendar, note, or project source, then ask for the change review again.
                """;
        }

        if (string.Equals(context.TemplateUsed, "plan_summary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "plan_request", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "planning", StringComparison.OrdinalIgnoreCase))
        {
            return """
                Priorities:
                1. I do not have current task, calendar, or project details to build a reliable plan safely.

                Risks:
                1. The main risk is missing or stale context.

                Suggestions:
                1. Pick one known priority, confirm the next deadline, then ask again once current task or calendar details are available.
                """;
        }

        return "I do not have enough governed evidence to answer that safely. Please name the source domain, runbook, repo area, log, or CWI component you want me to use, and I can retry with governed retrieval.";
    }

    public static string BuildModelUnavailable(AskWorkflowContext context)
    {
        if (string.Equals(context.TemplateUsed, "operator_troubleshooting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "troubleshooting", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTroubleshootingFallback(context);
        }

        if (context.EvidenceRequired)
        {
            return BuildEvidenceUnavailable(context);
        }

        return "I can still help, but the model did not return a usable response. Please retry with the target system, expected outcome, and any error text; I will keep private source retrieval blocked unless you name the source domain.";
    }

    public static string BuildEmptyAnswer(AskWorkflowContext context)
    {
        return BuildModelUnavailable(context);
    }

    private static string BuildTroubleshootingFallback(AskWorkflowContext context)
    {
        if (MentionsAny(context, "docker", "container", "curl"))
        {
            return """
                Start here:

                1. Confirm the container is running and whether it restarted recently.
                2. Check the last container logs for the first error before the restart.
                3. Verify the service is listening on the expected port inside the container.
                4. Run curl with verbose output against the exact health URL.
                5. If curl fails, compare container port mapping, DNS, firewall, and environment variables before restarting again.

                Verification command:
                ```bash
                docker ps --format "{{.Names}} {{.Status}} {{.Ports}}"
                docker logs --tail 100 <container>
                curl -v <health-url>
                ```
                """;
        }

        return """
            Got you - something broke.

            Start here:

            1. Capture the exact failure message and timestamp.
            2. Check the most recent logs before and after the failure.
            3. Verify the dependent service is reachable from the failing runtime.
            4. Confirm configuration, credentials, network route, and health check path.
            5. Retry one controlled change at a time and record the result.

            Verification command:
            ```bash
            echo "replace with the service-specific health check"
            ```
            """;
    }

    private static bool MentionsAny(AskWorkflowContext context, params string[] terms)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        return terms.Any(term =>
            primaryQuestion.Contains(term, StringComparison.OrdinalIgnoreCase)
            || context.RetrievalDomainTerms.Any(domainTerm => string.Equals(domainTerm, term, StringComparison.OrdinalIgnoreCase)));
    }
}
#nullable disable
