#nullable enable
using Cwi.Orchestrator.Application.Guardrails.Parsing;
using Cwi.Orchestrator.Application.Guardrails.Units;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Retrieval;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class DiscrepancyEvaluator
{
    private const double MediumRatio = 1.25;
    private const double HighRatio = 2.0;
    private const double SemanticOverlapThreshold = 0.15;

    public DiscrepancyEvaluation Evaluate(string? answer, IReadOnlyList<RetrievalResult> sources)
    {
        if (sources.Count == 0)
        {
            return new DiscrepancyEvaluation
            {
                Severity = "low",
                Reason = "No sources available.",
                Alignment = BuildAlignment(sources)
            };
        }

        var sourceQuantities = ExtractSourceQuantities(sources);
        var baseReason = sourceQuantities.Count == 0
            ? "No quantitative evidence detected."
            : null;

        var answerQuantities = ExtractAnswerQuantities(answer);
        var discrepancies = new List<DiscrepancyRecord>();
        var corrections = new List<CorrectionDiff>();
        var severity = "low";
        var score = 0.0;

        foreach (var entry in sourceQuantities)
        {
            var dimension = entry.Key;
            var values = entry.Value;
            if (values.Count < 2)
            {
                continue;
            }

            values.Sort();
            var min = values.First();
            var max = values.Last();
            var ratio = max / Math.Max(min, 1e-6);
            var median = values[values.Count / 2];

            if (ratio >= MediumRatio)
            {
                discrepancies.Add(new DiscrepancyRecord
                {
                    Field = dimension.ToString().ToLowerInvariant(),
                    Category = "numeric",
                    Expected = $"{median:0.###} {dimension.ToString().ToLowerInvariant()}",
                    Actual = $"{min:0.###}..{max:0.###} {dimension.ToString().ToLowerInvariant()}",
                    Note = "Sources disagree on magnitude."
                });
                var derivedSeverity = ratio >= HighRatio ? "high" : "medium";
                severity = MaxSeverity(severity, derivedSeverity);
                score = Math.Max(score, derivedSeverity == "high" ? 0.9 : 0.6);
            }

            if (answerQuantities.TryGetValue(dimension, out var answerValue))
            {
                var deviation = Math.Abs(answerValue - median) / Math.Max(Math.Abs(median), 1e-6);
                if (deviation >= 0.25)
                {
                    discrepancies.Add(new DiscrepancyRecord
                    {
                        Field = dimension.ToString().ToLowerInvariant(),
                        Category = "numeric",
                        Expected = $"{median:0.###} {dimension.ToString().ToLowerInvariant()}",
                        Actual = $"{answerValue:0.###} {dimension.ToString().ToLowerInvariant()}",
                        Note = "Answer deviates from source consensus."
                    });

                    corrections.Add(new CorrectionDiff
                    {
                        Field = dimension.ToString().ToLowerInvariant(),
                        From = $"{answerValue:0.###} {dimension.ToString().ToLowerInvariant()}",
                        To = $"{median:0.###} {dimension.ToString().ToLowerInvariant()}",
                        Note = "Suggested correction based on source consensus."
                    });

                    if (deviation >= 0.5)
                    {
                        severity = MaxSeverity(severity, "high");
                        score = Math.Max(score, 0.9);
                    }
                    else if (severity == "low")
                    {
                        severity = "medium";
                        score = Math.Max(score, 0.6);
                    }
                }
            }
        }

        var logicalDiscrepancies = DetectLogicalContradictions(sources);
        if (logicalDiscrepancies.Count > 0)
        {
            discrepancies.AddRange(logicalDiscrepancies);
            severity = MaxSeverity(severity, "high");
            score = Math.Max(score, 1.0);
        }

        if (DetectSemanticMismatch(answer, sources))
        {
            discrepancies.Add(new DiscrepancyRecord
            {
                Field = "semantic_overlap",
                Category = "semantic",
                Expected = "Answer aligns with source terminology.",
                Actual = "Answer shares limited overlap with sources.",
                Note = "Low semantic overlap detected between sources and answer."
            });
            severity = MaxSeverity(severity, "medium");
            score = Math.Max(score, 0.5);
        }

        if (discrepancies.Count == 0)
        {
            return new DiscrepancyEvaluation
            {
                Severity = "low",
                Reason = baseReason ?? "No discrepancy detected.",
                Alignment = BuildAlignment(sources),
                Score = 0.0
            };
        }

        return new DiscrepancyEvaluation
        {
            Severity = severity,
            Reason = "Discrepancies detected between sources and answer.",
            Discrepancies = discrepancies,
            Corrections = corrections,
            Alignment = BuildAlignment(sources),
            FallbackAnswer = severity == "high"
                ? "Sources conflict on critical details. Request approval or provide additional evidence."
                : null,
            Score = score
        };
    }

    private static Dictionary<Dimension, List<double>> ExtractSourceQuantities(IReadOnlyList<RetrievalResult> sources)
    {
        var results = new Dictionary<Dimension, List<double>>();
        foreach (var source in sources)
        {
            var quantities = QuantityExtractor.Extract(source.Content);
            foreach (var quantity in quantities)
            {
                var normalized = UnitNormalizer.Normalize(quantity);
                if (!normalized.Success || normalized.Normalized is null)
                {
                    continue;
                }

                var normalizedQuantity = normalized.Normalized.Value;
                if (!results.TryGetValue(normalizedQuantity.Dimension, out var list))
                {
                    list = new List<double>();
                    results[normalizedQuantity.Dimension] = list;
                }

                list.Add(normalizedQuantity.Value);
            }
        }

        return results;
    }

    private static MultiSourceAlignmentDto BuildAlignment(IReadOnlyList<RetrievalResult> sources)
    {
        var inputs = sources.Select(source => new AlignmentInputDto
        {
            SourceId = source.DocId,
            RawOutput = source.Content
        }).ToList();

        var normalized = new List<NormalizedFieldDto>();
        var alignmentMap = new List<AlignmentMapEntryDto>();

        foreach (var source in sources)
        {
            foreach (var quantity in QuantityExtractor.Extract(source.Content))
            {
                var normalizedResult = UnitNormalizer.Normalize(quantity);
                if (!normalizedResult.Success || normalizedResult.Normalized is null)
                {
                    continue;
                }

                var normalizedQuantity = normalizedResult.Normalized.Value;
                var field = normalizedQuantity.Dimension.ToString().ToLowerInvariant();
                normalized.Add(new NormalizedFieldDto
                {
                    Field = field,
                    Value = normalizedQuantity.Value,
                    Unit = normalizedQuantity.Unit,
                    Confidence = 0.8
                });

                alignmentMap.Add(new AlignmentMapEntryDto
                {
                    SourceId = source.DocId,
                    Field = quantity.Unit,
                    NormalizedField = field
                });
            }
        }

        return new MultiSourceAlignmentDto
        {
            Inputs = inputs,
            Normalized = normalized,
            AlignmentMap = alignmentMap,
            Ambiguities = Array.Empty<AlignmentAmbiguityDto>()
        };
    }

    private static Dictionary<Dimension, double> ExtractAnswerQuantities(string? answer)
    {
        var results = new Dictionary<Dimension, double>();
        if (string.IsNullOrWhiteSpace(answer))
        {
            return results;
        }

        var quantities = QuantityExtractor.Extract(answer);
        foreach (var quantity in quantities)
        {
            var normalized = UnitNormalizer.Normalize(quantity);
            if (!normalized.Success || normalized.Normalized is null)
            {
                continue;
            }

            var normalizedQuantity = normalized.Normalized.Value;
            results[normalizedQuantity.Dimension] = normalizedQuantity.Value;
        }

        return results;
    }

    private static List<DiscrepancyRecord> DetectLogicalContradictions(IReadOnlyList<RetrievalResult> sources)
    {
        var content = string.Join(" ", sources.Select(source => source.Content ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<DiscrepancyRecord>();
        }

        var pairs = new List<(string on, string off)>
        {
            ("yes", "no"),
            ("true", "false"),
            ("enabled", "disabled"),
            ("allow", "deny"),
            ("on", "off")
        };

        foreach (var pair in pairs)
        {
            if (content.Contains(pair.on, StringComparison.OrdinalIgnoreCase) &&
                content.Contains(pair.off, StringComparison.OrdinalIgnoreCase))
            {
                return new List<DiscrepancyRecord>
                {
                    new()
                    {
                        Field = "logical_state",
                        Category = "logical",
                        Expected = pair.on,
                        Actual = pair.off,
                        Note = "Sources contain contradictory logical states."
                    }
                };
            }
        }

        return new List<DiscrepancyRecord>();
    }

    private static bool DetectSemanticMismatch(string? answer, IReadOnlyList<RetrievalResult> sources)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var sourceText = string.Join(" ", sources.Select(source => source.Content ?? string.Empty));
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var sourceTerms = Tokenize(sourceText);
        var answerTerms = Tokenize(answer);
        if (sourceTerms.Count == 0 || answerTerms.Count == 0)
        {
            return false;
        }

        var overlap = answerTerms.Count(term => sourceTerms.Contains(term));
        var ratio = overlap / (double)answerTerms.Count;
        return ratio < SemanticOverlapThreshold;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '\"', '\'', '(', ')', '[', ']', '{', '}' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = raw.Trim().ToLowerInvariant();
            if (trimmed.Length < 4)
            {
                continue;
            }

            tokens.Add(trimmed);
        }

        return tokens;
    }

    private static string MaxSeverity(string current, string candidate)
    {
        var currentRank = SeverityRank(current);
        var candidateRank = SeverityRank(candidate);
        return candidateRank > currentRank ? candidate : current;
    }

    private static int SeverityRank(string severity)
    {
        return severity.Trim().ToLowerInvariant() switch
        {
            "low" => 1,
            "medium" => 2,
            "high" => 3,
            _ => 0
        };
    }
}
#nullable disable
