using System.Text;
using System.Text.Json;

namespace Cwi.Router.Application.Routing;

public sealed class ComplexityClassifier
{
    private static readonly char[] WordSeparators = { ' ', '\t', '\r', '\n' };

    public bool TryClassify(string path, string? body, out ComplexityDescriptor descriptor)
    {
        descriptor = new ComplexityDescriptor();

        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        if (!path.Contains("/api/ask", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains("/api/action", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryParseJson(body, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var payloadBytes = Encoding.UTF8.GetByteCount(body);

            if (path.Contains("/api/ask", StringComparison.OrdinalIgnoreCase))
            {
                var question = GetString(root, "question") ?? string.Empty;
                var wordCount = CountWords(question);
                var charCount = question.Length;
                var topK = GetInt(root, "context", "top_k");

                var score = ComputeScore(wordCount, charCount, payloadBytes, topK);
                descriptor = new ComplexityDescriptor
                {
                    RequestKind = "ask",
                    WordCount = wordCount,
                    CharacterCount = charCount,
                    PayloadBytes = payloadBytes,
                    TopK = topK,
                    TokenEstimate = EstimateTokens(wordCount, charCount),
                    Score = score,
                    Level = ClassifyLevel(score)
                };
                return true;
            }

            var actionType = GetString(root, "action", "type");
            var scoreAction = ComputeActionScore(root, payloadBytes);
            descriptor = new ComplexityDescriptor
            {
                RequestKind = "action",
                WordCount = 0,
                CharacterCount = 0,
                PayloadBytes = payloadBytes,
                ActionType = actionType,
                TokenEstimate = EstimateTokens(0, 0),
                Score = scoreAction,
                Level = ClassifyLevel(scoreAction)
            };
            return true;
        }
    }

    private static bool TryParseJson(string body, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static int CountWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return 0;
        }

        var parts = input.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length;
    }

    private static double EstimateTokens(int wordCount, int charCount)
    {
        if (wordCount == 0 && charCount == 0)
        {
            return 0;
        }

        return Math.Max(wordCount * 1.3, charCount / 4.0);
    }

    private static double ComputeScore(int wordCount, int charCount, int payloadBytes, int? topK)
    {
        var wordScore = Math.Min(1.0, wordCount / 250.0);
        var charScore = Math.Min(1.0, charCount / 2000.0);
        var payloadScore = Math.Min(1.0, payloadBytes / 6000.0);
        var topKScore = topK.HasValue ? Math.Min(1.0, topK.Value / 10.0) : 0;
        var total = wordScore + charScore + payloadScore + topKScore;
        return Math.Min(1.0, total / 4.0);
    }

    private static double ComputeActionScore(JsonElement root, int payloadBytes)
    {
        var risk = GetString(root, "action", "payload", "risk") ??
            GetString(root, "action", "payload", "risk_level");

        if (string.Equals(risk, "high", StringComparison.OrdinalIgnoreCase))
        {
            return 0.9;
        }

        if (payloadBytes >= 6000)
        {
            return 0.7;
        }

        if (payloadBytes >= 2000)
        {
            return 0.45;
        }

        return 0.2;
    }

    private static ComplexityLevel ClassifyLevel(double score)
    {
        if (score >= 0.67)
        {
            return ComplexityLevel.High;
        }

        if (score >= 0.34)
        {
            return ComplexityLevel.Medium;
        }

        return ComplexityLevel.Low;
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var child) ||
            child.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return child.GetString();
    }

    private static string? GetString(JsonElement root, string parent, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(parent, out var parentElement) ||
            parentElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parentElement, property);
    }

    private static string? GetString(JsonElement root, string parent, string child, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(parent, out var parentElement) ||
            parentElement.ValueKind != JsonValueKind.Object ||
            !parentElement.TryGetProperty(child, out var childElement) ||
            childElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(childElement, property);
    }

    private static int? GetInt(JsonElement root, string parent, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(parent, out var parentElement) ||
            parentElement.ValueKind != JsonValueKind.Object ||
            !parentElement.TryGetProperty(property, out var child))
        {
            return null;
        }

        if (child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var value))
        {
            return value;
        }

        if (child.ValueKind == JsonValueKind.String && int.TryParse(child.GetString(), out value))
        {
            return value;
        }

        return null;
    }
}
