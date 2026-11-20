#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskDeterministicDirectRouteStep : IAskWorkflowStep
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    private static readonly IReadOnlyDictionary<string, string> CityTimeZones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["chicago"] = "America/Chicago",
        ["houston"] = "America/Chicago",
        ["dallas"] = "America/Chicago",
        ["austin"] = "America/Chicago",
        ["san antonio"] = "America/Chicago",
        ["new york"] = "America/New_York",
        ["washington"] = "America/New_York",
        ["atlanta"] = "America/New_York",
        ["miami"] = "America/New_York",
        ["denver"] = "America/Denver",
        ["phoenix"] = "America/Phoenix",
        ["los angeles"] = "America/Los_Angeles",
        ["la"] = "America/Los_Angeles",
        ["san francisco"] = "America/Los_Angeles",
        ["seattle"] = "America/Los_Angeles"
    };

    private static readonly IReadOnlyDictionary<string, string> WindowsTimeZoneIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Chicago"] = "Central Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Phoenix"] = "US Mountain Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["UTC"] = "UTC"
    };

    private static readonly IReadOnlyDictionary<string, (string Standard, string Daylight)> TimeZoneAbbreviations =
        new Dictionary<string, (string Standard, string Daylight)>(StringComparer.OrdinalIgnoreCase)
        {
            ["America/Chicago"] = ("CST", "CDT"),
            ["Central Standard Time"] = ("CST", "CDT"),
            ["America/New_York"] = ("EST", "EDT"),
            ["Eastern Standard Time"] = ("EST", "EDT"),
            ["America/Denver"] = ("MST", "MDT"),
            ["Mountain Standard Time"] = ("MST", "MDT"),
            ["America/Phoenix"] = ("MST", "MST"),
            ["US Mountain Standard Time"] = ("MST", "MST"),
            ["America/Los_Angeles"] = ("PST", "PDT"),
            ["Pacific Standard Time"] = ("PST", "PDT"),
            ["UTC"] = ("UTC", "UTC")
        };

    private readonly JourneyRequestContext _journeyRequestContext;
    private readonly TraceLookupStore _traceStore;
    private readonly ResponseIntelligenceOverlayService _responseIntelligenceOverlay;

    public AskDeterministicDirectRouteStep(
        JourneyRequestContext journeyRequestContext,
        TraceLookupStore traceStore,
        ResponseIntelligenceOverlayService? responseIntelligenceOverlay = null)
    {
        _journeyRequestContext = journeyRequestContext;
        _traceStore = traceStore;
        _responseIntelligenceOverlay = responseIntelligenceOverlay ?? new ResponseIntelligenceOverlayService();
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!TryBuildDeterministicAnswer(
                context.RoutingQuestion,
                context.EffectiveTimezone,
                DateTimeOffset.UtcNow,
                out var deterministic))
        {
            return next();
        }

        ApplyDirectAnswer(context, deterministic);
        _responseIntelligenceOverlay.Apply(context);
        var response = AskResponseFactory.Create(context, _journeyRequestContext, _traceStore);
        return Task.FromResult(ServiceResponse<AskResponseDto>.Success(response));
    }

    public static bool TryBuildDeterministicAnswer(
        string? question,
        string? effectiveTimezone,
        DateTimeOffset utcNow,
        out DeterministicDirectAnswer answer)
    {
        answer = new DeterministicDirectAnswer(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
        var primaryQuestion = LanguageNormalizer.NormalizeForRouting(AskQuestionScope.ExtractPrimaryQuestion(question));
        if (string.IsNullOrWhiteSpace(primaryQuestion))
        {
            return false;
        }

        if (TryBuildArithmeticAnswer(primaryQuestion, out var arithmeticAnswer))
        {
            answer = new DeterministicDirectAnswer(
                arithmeticAnswer,
                "deterministic_reasoning",
                "deterministic_reasoning",
                "deterministic-reasoner",
                "non_grounded",
                "deterministic_reasoning_does_not_require_sources",
                "direct_answer");
            return true;
        }

        if (TryResolveDateTimeIntent(primaryQuestion, out var dateTimeIntent))
        {
            answer = BuildDateTimeAnswer(primaryQuestion, effectiveTimezone, utcNow, dateTimeIntent);
            return true;
        }

        return false;
    }

    private static void ApplyDirectAnswer(AskWorkflowContext context, DeterministicDirectAnswer deterministic)
    {
        context.Answer = deterministic.Answer;
        context.Route = "deterministic_direct_answer";
        context.EntityFound = null;
        context.QueryClassification = deterministic.Classification;
        context.QueryClassificationReason = "Matched deterministic direct-answer route before pack, retrieval, memory, graph, or model generation.";
        context.IntentType = deterministic.IntentType;
        context.TemplateUsed = deterministic.TemplateUsed;
        context.ModelName = deterministic.ModelName;
        context.SkipModelInference = true;
        context.ExecutionTarget = "deterministic_runtime";
        context.GroundingMode = deterministic.GroundingMode;
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = deterministic.RetrievalSkippedReason;
        context.EvidenceRequired = false;
        context.Retrieved.Clear();
        context.GraphEvidence.Clear();
        context.MemoryResults = Array.Empty<GovernedMemoryResult>();
        context.SourceResults = Array.Empty<GovernedSourceResult>();
    }

    private static bool TryResolveDateTimeIntent(string question, out string intent)
    {
        intent = string.Empty;
        var normalized = Normalize(question).TrimEnd('?', '.', '!');

        if (!LooksLikeDateOrTimeQuestion(normalized))
        {
            return false;
        }

        var asksDate = normalized.Contains("date", StringComparison.Ordinal)
            || normalized.Contains("today", StringComparison.Ordinal)
            || normalized.Contains("todays", StringComparison.Ordinal)
            || normalized.Contains("today's", StringComparison.Ordinal);
        var asksTime = normalized.Contains("time", StringComparison.Ordinal);
        var asksDay = normalized.Contains("what day", StringComparison.Ordinal)
            || normalized.Contains("day of the week", StringComparison.Ordinal);

        if (asksDate && asksTime)
        {
            intent = "current_datetime_intent";
            return true;
        }

        if (asksTime)
        {
            intent = "current_time_intent";
            return true;
        }

        if (asksDay && !asksDate)
        {
            intent = "current_day_intent";
            return true;
        }

        if (asksDate || asksDay)
        {
            intent = "current_date_intent";
            return true;
        }

        return false;
    }

    private static bool LooksLikeDateOrTimeQuestion(string normalized)
    {
        if (normalized.Length > 120)
        {
            return false;
        }

        return normalized is "what time is it" or "what is the time" or "what is the current time" or "current time"
               || normalized is "what is todays date" or "what is today's date" or "what is the current date" or "what is the date"
               || normalized is "todays date" or "today's date"
               || normalized is "what date is it" or "current date" or "what day is it"
               || normalized is "what day is today" or "what is the current date and time"
               || normalized.Contains("today's date and time", StringComparison.Ordinal)
               || normalized.Contains("todays date and time", StringComparison.Ordinal)
               || normalized.StartsWith("what time is it in ", StringComparison.Ordinal)
               || normalized.StartsWith("what date is it in ", StringComparison.Ordinal)
               || normalized.StartsWith("what is the date in ", StringComparison.Ordinal)
               || normalized.StartsWith("what is the time in ", StringComparison.Ordinal)
               || normalized.StartsWith("current date and time", StringComparison.Ordinal);
    }

    private static DeterministicDirectAnswer BuildDateTimeAnswer(
        string question,
        string? effectiveTimezone,
        DateTimeOffset utcNow,
        string intent)
    {
        var requestedTimezone = ResolveRequestedTimezone(question, effectiveTimezone);
        var timezone = FindTimeZone(requestedTimezone) ?? TimeZoneInfo.Utc;
        var localNow = TimeZoneInfo.ConvertTime(utcNow, timezone);
        var abbreviation = ResolveAbbreviation(timezone, requestedTimezone, localNow);
        var localizedDate = localNow.ToString("dddd, MMMM d, yyyy", UsCulture);
        var localizedDay = localNow.ToString("dddd", UsCulture);
        var localizedTime = $"{localNow.ToString("h:mm tt", UsCulture)} {abbreviation}".Trim();
        var response = intent switch
        {
            "current_day_intent" => $"Today is {localizedDay}.",
            "current_time_intent" => $"It is {localizedTime}.",
            "current_datetime_intent" => $"It is {localizedDate} at {localizedTime}.",
            _ => $"Today is {localizedDate}."
        };

        return new DeterministicDirectAnswer(
            response,
            "deterministic_datetime",
            intent,
            "deterministic-clock",
            "non_grounded",
            "deterministic_datetime_uses_server_clock",
            "direct_answer");
    }

    private static string ResolveRequestedTimezone(string question, string? effectiveTimezone)
    {
        var normalized = Normalize(question).TrimEnd('?', '.', '!');
        foreach (var entry in CityTimeZones)
        {
            if (normalized.EndsWith($" in {entry.Key}", StringComparison.Ordinal)
                || normalized.Contains($" in {entry.Key} ", StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return string.IsNullOrWhiteSpace(effectiveTimezone) ? "UTC" : effectiveTimezone.Trim();
    }

    private static TimeZoneInfo? FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        if (!WindowsTimeZoneIds.TryGetValue(id, out var windowsId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static string ResolveAbbreviation(TimeZoneInfo timezone, string requestedTimezone, DateTimeOffset localNow)
    {
        var lookupIds = new[] { requestedTimezone, timezone.Id };
        foreach (var lookupId in lookupIds)
        {
            if (!TimeZoneAbbreviations.TryGetValue(lookupId, out var abbreviations))
            {
                continue;
            }

            return timezone.IsDaylightSavingTime(localNow) ? abbreviations.Daylight : abbreviations.Standard;
        }

        return timezone.Id.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            ? "UTC"
            : timezone.StandardName;
    }

    private static bool TryBuildArithmeticAnswer(string question, out string answer)
    {
        answer = string.Empty;
        var normalized = NormalizeArithmetic(question);
        var match = Regex.Match(
            normalized,
            @"^(?:what\s+is|calculate|compute|solve)?\s*(?<left>[-+]?\d+(?:\.\d+)?)\s*(?<op>\+|-|\*|/|x)\s*(?<right>[-+]?\d+(?:\.\d+)?)\s*\??$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        if (!decimal.TryParse(match.Groups["left"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var left)
            || !decimal.TryParse(match.Groups["right"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var right))
        {
            return false;
        }

        var op = match.Groups["op"].Value.ToLowerInvariant();
        decimal result;
        switch (op)
        {
            case "+":
                result = left + right;
                break;
            case "-":
                result = left - right;
                break;
            case "*":
            case "x":
                result = left * right;
                break;
            case "/":
                if (right == 0)
                {
                    answer = "I could not parse that calculation. Please rewrite it as a simple expression.";
                    return true;
                }

                result = left / right;
                break;
            default:
                return false;
        }

        answer = $"{FormatNumber(left)} {FormatOperator(op)} {FormatNumber(right)} = {FormatNumber(result)}.";
        return true;
    }

    private static string NormalizeArithmetic(string value)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(value);
        normalized = normalized
            .Replace("what is the sum of ", "what is ", StringComparison.Ordinal)
            .Replace("what's the sum of ", "what is ", StringComparison.Ordinal)
            .Replace("calculate the sum of ", "calculate ", StringComparison.Ordinal)
            .Replace("compute the sum of ", "compute ", StringComparison.Ordinal)
            .Replace("solve the sum of ", "solve ", StringComparison.Ordinal);

        return normalized
            .Replace(" plus ", " + ", StringComparison.Ordinal)
            .Replace(" minus ", " - ", StringComparison.Ordinal)
            .Replace(" times ", " * ", StringComparison.Ordinal)
            .Replace(" multiplied by ", " * ", StringComparison.Ordinal)
            .Replace(" divided by ", " / ", StringComparison.Ordinal)
            .Replace(" over ", " / ", StringComparison.Ordinal);
    }

    private static string FormatOperator(string op)
    {
        return op == "x" ? "*" : op;
    }

    private static string FormatNumber(decimal value)
    {
        return value % 1 == 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.############", CultureInfo.InvariantCulture);
    }

    private static string Normalize(string value)
    {
        return LanguageNormalizer.NormalizeForRouting(value);
    }

    public readonly record struct DeterministicDirectAnswer(
        string Answer,
        string Classification,
        string IntentType,
        string ModelName,
        string GroundingMode,
        string RetrievalSkippedReason,
        string TemplateUsed);
}
#nullable disable

