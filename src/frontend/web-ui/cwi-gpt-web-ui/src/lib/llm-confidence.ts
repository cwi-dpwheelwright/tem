interface ParsedAnswer {
  answer: string;
  confidence: number | null;
  sourcesCount: number | null;
}

const META_PATTERN = /\s*\[\[confidence:([0-9]+(?:\.[0-9]+)?)(?:\s+sources:([0-9]+))?\]\]\s*$/i;

export function buildQuestionWithConfidence(question: string): string {
  return question;
}

export function parseAnswerWithConfidence(answer: string): ParsedAnswer {
  if (!answer) {
    return { answer: "", confidence: null, sourcesCount: null };
  }

  const match = answer.match(META_PATTERN);
  if (!match) {
    return { answer, confidence: null, sourcesCount: null };
  }

  const confidenceRaw = match[1];
  const sourcesRaw = match[2];
  const confidence = confidenceRaw ? Number(confidenceRaw) : null;
  const sourcesCount = sourcesRaw ? Number(sourcesRaw) : null;

  const cleaned = answer.replace(META_PATTERN, "").trim();

  return {
    answer: cleaned,
    confidence: Number.isFinite(confidence) ? confidence : null,
    sourcesCount: Number.isFinite(sourcesCount) ? sourcesCount : null,
  };
}
