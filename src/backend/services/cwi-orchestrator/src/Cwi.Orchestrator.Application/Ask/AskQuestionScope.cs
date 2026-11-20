#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cwi.Orchestrator.Application.Ask;

public static class AskQuestionScope
{
    private const string PrimaryQuestionHeader = "Primary question:";
    private const string AuthoritativeMarker = "The primary question is authoritative.";
    private static readonly string[] LeadingQuestionHeaders =
    {
        PrimaryQuestionHeader,
        "Current question:",
        "Current user query:",
        "Current user prompt:",
        "User question:",
        "Question:",
        "Prompt:"
    };

    private static readonly string[] ContextSectionHeaders =
    {
        AuthoritativeMarker,
        "Conversation context from this chat:",
        "Recent conversation:",
        "Chat history:",
        "Imported external chat history",
        "Attached files and images:",
        "Relevant workspace Sources selected for this prompt",
        "Response constraints:",
        "Answer the primary question directly"
    };

    private static readonly string[] RolePrefixes =
    {
        "User:",
        "Human:",
        "Current user:",
        "Current user query:",
        "Current question:",
        "Question:"
    };

    private static readonly string[] NonQuestionLinePrefixes =
    {
        "Assistant:",
        "System:",
        "Developer:",
        "CWI:",
        "Answer the primary question directly",
        "Conversation context from this chat:",
        "Recent conversation:",
        "Chat history:",
        "Imported external chat history",
        "Attached files and images:",
        "Relevant workspace Sources selected for this prompt",
        "Response constraints:"
    };

    public static string ExtractPrimaryQuestion(string? question)
    {
        var normalized = NormalizeNewlines(question);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (TryExtractLeadingQuestionSection(normalized, out var primaryQuestion))
        {
            return primaryQuestion;
        }

        if (TryExtractLastRoleQuestion(normalized, out var roleQuestion))
        {
            return roleQuestion;
        }

        if (LooksLikeConversationContext(normalized)
            && TryExtractLastQuestionLikeLine(normalized, out var finalQuestion))
        {
            return finalQuestion;
        }

        return normalized.Trim();
    }

    public static string ExtractSupportingContext(string? question)
    {
        var normalized = NormalizeNewlines(question);
        if (string.IsNullOrWhiteSpace(normalized)
            || !normalized.StartsWith(PrimaryQuestionHeader, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var remainder = normalized[PrimaryQuestionHeader.Length..].TrimStart();
        var boundary = FindQuestionBoundary(remainder);
        if (boundary < 0 || boundary >= remainder.Length)
        {
            return string.Empty;
        }

        var context = remainder[boundary..].TrimStart();
        if (context.StartsWith(AuthoritativeMarker, StringComparison.OrdinalIgnoreCase))
        {
            var firstBlankLine = context.IndexOf("\n\n", StringComparison.Ordinal);
            if (firstBlankLine >= 0)
            {
                context = context[(firstBlankLine + 2)..].TrimStart();
            }
        }

        return context.Trim();
    }

    private static bool TryExtractLeadingQuestionSection(string normalized, out string primaryQuestion)
    {
        primaryQuestion = string.Empty;
        foreach (var header in LeadingQuestionHeaders)
        {
            if (!normalized.StartsWith(header, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = normalized[header.Length..].TrimStart();
            if (string.IsNullOrWhiteSpace(remainder))
            {
                return false;
            }

            var boundary = FindQuestionBoundary(remainder);
            primaryQuestion = (boundary >= 0 ? remainder[..boundary] : remainder).Trim();
            return !string.IsNullOrWhiteSpace(primaryQuestion);
        }

        return false;
    }

    private static int FindQuestionBoundary(string value)
    {
        var boundary = value.IndexOf("\n\n", StringComparison.Ordinal);
        foreach (var header in ContextSectionHeaders)
        {
            var markerIndex = IndexOfIgnoreCase(value, $"\n{header}");
            if (markerIndex >= 0)
            {
                boundary = boundary < 0 ? markerIndex : Math.Min(boundary, markerIndex);
            }

            markerIndex = IndexOfIgnoreCase(value, $"\n\n{header}");
            if (markerIndex >= 0)
            {
                boundary = boundary < 0 ? markerIndex : Math.Min(boundary, markerIndex);
            }
        }

        return boundary;
    }

    private static bool TryExtractLastRoleQuestion(string normalized, out string roleQuestion)
    {
        roleQuestion = string.Empty;
        var lines = normalized.Split('\n');
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            foreach (var prefix in RolePrefixes)
            {
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = line[prefix.Length..].Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = CollectFollowingLines(lines, index + 1);
                }

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    roleQuestion = candidate.Trim();
                    return true;
                }
            }
        }

        return false;
    }

    private static string CollectFollowingLines(string[] lines, int startIndex)
    {
        var collected = new List<string>();
        for (var index = startIndex; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                if (collected.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (StartsWithAny(line, RolePrefixes) || StartsWithAny(line, NonQuestionLinePrefixes))
            {
                break;
            }

            collected.Add(line);
        }

        return string.Join('\n', collected).Trim();
    }

    private static bool TryExtractLastQuestionLikeLine(string normalized, out string finalQuestion)
    {
        finalQuestion = string.Empty;
        foreach (var line in normalized.Split('\n').Reverse())
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || StartsWithAny(trimmed, NonQuestionLinePrefixes))
            {
                continue;
            }

            finalQuestion = trimmed;
            return true;
        }

        return false;
    }

    private static bool LooksLikeConversationContext(string normalized)
    {
        return ContainsIgnoreCase(normalized, "\nUser:")
               || ContainsIgnoreCase(normalized, "\nHuman:")
               || ContainsIgnoreCase(normalized, "\nAssistant:")
               || ContainsIgnoreCase(normalized, "Conversation context from this chat:")
               || ContainsIgnoreCase(normalized, "Recent conversation:")
               || ContainsIgnoreCase(normalized, "Chat history:");
    }

    private static bool StartsWithAny(string value, IReadOnlyList<string> prefixes)
    {
        return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeNewlines(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
    }

    private static int IndexOfIgnoreCase(string value, string search)
    {
        return value.IndexOf(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string value, string search)
    {
        return value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
#nullable disable
