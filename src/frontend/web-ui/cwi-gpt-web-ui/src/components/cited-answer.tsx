"use client";

import type React from "react";
import type { AskCitation, Trace } from "@/lib/api/types";

type CitedAnswerProps = {
  answer: string;
  citations?: AskCitation[];
  trace?: Trace | null;
  className?: string;
};

type CitationSourcesProps = {
  citations?: AskCitation[];
  trace?: Trace | null;
  compact?: boolean;
};

type DisplaySource = {
  key: string;
  kind: SourceKind;
  marker?: number;
  title: string;
  url?: string;
  excerpt?: string;
  sourceId?: string;
  contentSha256?: string;
};

type SourceKind =
  | "trusted_web"
  | "memory"
  | "document"
  | "graph"
  | "pack"
  | "tool"
  | "home_node"
  | "other";

type SourceGroup = {
  key: SourceKind;
  label: string;
  sources: DisplaySource[];
};

type AnswerBlock =
  | {
      type: "paragraph";
      text: string;
    }
  | {
      type: "heading";
      level: number;
      text: string;
    }
  | {
      type: "unordered-list" | "ordered-list";
      items: string[];
    }
  | {
      type: "code";
      language?: string;
      code: string;
    };

const trustedWebFooterPattern = /(?:\r?\n){0,2}Trusted web sources (?:consulted|concluded):[^\n]*(?:\r?\n)?$/i;
const technicalSourceFooterPattern =
  /(?:\r?\n){0,2}Sources:\s*(?=.*(?:memory|source|pack|mcp_tool|mcp_source|home_node|web|graph|provenance):)[^\n]*(?:\r?\n)?$/i;
const inlineFormatPattern = /(\[(\d{1,2})\]|\*\*([^*\n]+)\*\*|==([^=\n]+)==|`([^`\n]+)`|\*([^*\n]+)\*|_([^_\n]+)_)/g;
const sourceKindOrder: SourceKind[] = [
  "trusted_web",
  "memory",
  "document",
  "graph",
  "pack",
  "tool",
  "home_node",
  "other",
];
const sourceKindLabels: Record<SourceKind, string> = {
  trusted_web: "Web",
  memory: "Memory",
  document: "Documents",
  graph: "Graph",
  pack: "Packs",
  tool: "Tools",
  home_node: "Home Nodes",
  other: "Other",
};

function buildDisplaySources(citations: AskCitation[] = [], trace?: Trace | null): DisplaySource[] {
  const sources = new Map<string, DisplaySource>();
  const sourceIds = new Set<string>();

  for (const citation of citations) {
    const markerKey = citation.marker ? `marker:${citation.marker}` : null;
    const sourceKey = citation.source_id ?? `${citation.doc_id}:${citation.chunk_id}`;
    const key = markerKey ?? sourceKey;
    addDisplaySource(sources, sourceIds, {
      key,
      kind: classifySource(sourceKey, citation.source_type, citation.url),
      marker: citation.marker ?? undefined,
      title: citation.title ?? citation.source_id ?? citation.doc_id,
      url: citation.url ?? undefined,
      excerpt: citation.excerpt ?? undefined,
      sourceId: citation.source_id ?? sourceKey,
      contentSha256: citation.content_sha256 ?? undefined,
    });
  }

  for (const source of trace?.trusted_web_sources ?? []) {
    const key = source.marker ? `marker:${source.marker}` : source.evidence_source_id ?? `${source.source_id}:${source.chunk_id}`;
    addDisplaySource(sources, sourceIds, {
      key,
      kind: "trusted_web",
      marker: source.marker ?? undefined,
      title: source.title || source.publisher || source.source_id,
      url: source.url,
      excerpt: source.excerpt,
      sourceId: source.evidence_source_id ?? `${source.source_id}:${source.chunk_id}`,
      contentSha256: source.content_sha256,
    });
  }

  for (const source of trace?.sources ?? []) {
    const displaySource = buildTraceSource(source);
    if (displaySource) {
      addDisplaySource(sources, sourceIds, displaySource);
    }
  }

  return Array.from(sources.values()).sort((left, right) => {
    const leftMarker = left.marker ?? Number.MAX_SAFE_INTEGER;
    const rightMarker = right.marker ?? Number.MAX_SAFE_INTEGER;
    const kindOrder = sourceKindOrder.indexOf(left.kind) - sourceKindOrder.indexOf(right.kind);
    return leftMarker - rightMarker || kindOrder || left.title.localeCompare(right.title);
  });
}

function addDisplaySource(
  sources: Map<string, DisplaySource>,
  sourceIds: Set<string>,
  source: DisplaySource,
): void {
  const normalizedIds = [source.sourceId, source.key]
    .filter((value): value is string => Boolean(value))
    .map(normalizeSourceId);
  if (normalizedIds.some((value) => sourceIds.has(value))) {
    return;
  }

  sources.set(source.key, source);
  for (const id of normalizedIds) {
    sourceIds.add(id);
  }
}

function normalizeSourceId(value: string): string {
  return value.trim().toLowerCase();
}

function classifySource(sourceId?: string | null, sourceType?: string | null, url?: string | null): SourceKind {
  const normalizedType = sourceType?.trim().toLowerCase() ?? "";
  const normalizedId = sourceId?.trim().toLowerCase() ?? "";

  if (
    normalizedType.includes("trusted_web")
    || normalizedId.startsWith("web:")
    || Boolean(url)
  ) {
    return "trusted_web";
  }

  if (normalizedId.startsWith("memory:") || normalizedType.includes("memory")) {
    return "memory";
  }

  if (normalizedId.startsWith("source:") || normalizedType.includes("document")) {
    return "document";
  }

  if (normalizedId.startsWith("graph:") || normalizedType.includes("graph")) {
    return "graph";
  }

  if (normalizedId.startsWith("pack:")) {
    return "pack";
  }

  if (normalizedId.startsWith("mcp_tool:") || normalizedId.startsWith("mcp_source:")) {
    return "tool";
  }

  if (normalizedId.startsWith("home_node:")) {
    return "home_node";
  }

  return "other";
}

function buildTraceSource(rawSource: string): DisplaySource | null {
  const raw = rawSource.trim();
  if (!raw) {
    return null;
  }

  if (raw.startsWith("mcp_source:")) {
    const source = raw.slice("mcp_source:".length);
    const [title, url] = source.split("|");
    return {
      key: raw,
      kind: url ? "trusted_web" : "tool",
      title: title || "MCP source",
      url: url || undefined,
      sourceId: raw,
    };
  }

  return {
    key: raw,
    kind: classifySource(raw),
    title: buildTraceSourceTitle(raw),
    sourceId: raw,
  };
}

function buildTraceSourceTitle(rawSource: string): string {
  if (rawSource.startsWith("memory:")) {
    return "Memory capsule";
  }

  if (rawSource.startsWith("source:")) {
    return "Document source";
  }

  if (rawSource.startsWith("pack:")) {
    return rawSource.slice("pack:".length).replace("@", " ");
  }

  if (rawSource.startsWith("mcp_tool:")) {
    return rawSource.slice("mcp_tool:".length);
  }

  if (rawSource.startsWith("home_node:")) {
    return rawSource.slice("home_node:".length);
  }

  if (rawSource.startsWith("web:")) {
    return "Trusted web source";
  }

  if (rawSource.startsWith("graph:")) {
    return "Graph evidence";
  }

  return rawSource;
}

function buildSourceLookup(sources: DisplaySource[]): Map<number, DisplaySource> {
  const lookup = new Map<number, DisplaySource>();
  for (const source of sources) {
    if (source.marker) {
      lookup.set(source.marker, source);
    }
  }
  return lookup;
}

function parseAnswerBlocks(answer: string): AnswerBlock[] {
  const normalized = answer.replace(/\r\n/g, "\n");
  const lines = normalized.split("\n");
  const blocks: AnswerBlock[] = [];
  let paragraphLines: string[] = [];
  let listBlock: Extract<AnswerBlock, { items: string[] }> | null = null;

  const flushParagraph = () => {
    const text = paragraphLines.join("\n").trim();
    if (text) {
      blocks.push({ type: "paragraph", text });
    }
    paragraphLines = [];
  };

  const flushList = () => {
    if (listBlock && listBlock.items.length > 0) {
      blocks.push(listBlock);
    }
    listBlock = null;
  };

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    const trimmed = line.trim();

    if (trimmed.startsWith("```")) {
      flushParagraph();
      flushList();
      const language = trimmed.slice(3).trim() || undefined;
      const codeLines: string[] = [];
      index += 1;
      while (index < lines.length && !lines[index].trim().startsWith("```")) {
        codeLines.push(lines[index]);
        index += 1;
      }
      blocks.push({ type: "code", language, code: codeLines.join("\n") });
      continue;
    }

    if (!trimmed) {
      flushParagraph();
      flushList();
      continue;
    }

    const headingMatch = trimmed.match(/^(#{1,4})\s+(.+)$/);
    if (headingMatch) {
      flushParagraph();
      flushList();
      blocks.push({
        type: "heading",
        level: headingMatch[1].length,
        text: headingMatch[2].trim(),
      });
      continue;
    }

    const unorderedMatch = line.match(/^\s*[-*]\s+(.+)$/);
    const orderedMatch = line.match(/^\s*\d+[.)]\s+(.+)$/);
    const listItemText = unorderedMatch?.[1] ?? orderedMatch?.[1];
    const listType = unorderedMatch ? "unordered-list" : orderedMatch ? "ordered-list" : null;

    if (listType && listItemText) {
      flushParagraph();
      if (!listBlock || listBlock.type !== listType) {
        flushList();
        listBlock = { type: listType, items: [] };
      }
      listBlock.items.push(listItemText.trim());
      continue;
    }

    if (listBlock && /^\s{2,}\S/.test(line)) {
      const lastItemIndex = listBlock.items.length - 1;
      if (lastItemIndex >= 0) {
        listBlock.items[lastItemIndex] = `${listBlock.items[lastItemIndex]} ${trimmed}`;
        continue;
      }
    }

    flushList();
    paragraphLines.push(line);
  }

  flushParagraph();
  flushList();
  return blocks;
}

function renderCitationMarker(
  marker: number,
  source: DisplaySource,
  key: string,
): React.ReactNode {
  const label = `[${marker}]`;
  return source.url ? (
    <a
      key={key}
      className="answer-citation-marker"
      href={source.url}
      target="_blank"
      rel="noreferrer"
      aria-label={`Open source ${marker}: ${source.title}`}
      title={source.title}
    >
      {label}
    </a>
  ) : (
    <span key={key} className="answer-citation-marker" title={source.title}>
      {label}
    </span>
  );
}

function renderInlineText(
  text: string,
  sourceLookup: Map<number, DisplaySource>,
  keyPrefix: string,
): React.ReactNode[] {
  const nodes: React.ReactNode[] = [];
  let cursor = 0;

  for (const match of text.matchAll(inlineFormatPattern)) {
    const matchIndex = match.index ?? 0;
    if (matchIndex > cursor) {
      nodes.push(text.slice(cursor, matchIndex));
    }

    const key = `${keyPrefix}-${matchIndex}`;
    if (match[2]) {
      const marker = Number.parseInt(match[2], 10);
      const source = sourceLookup.get(marker);
      nodes.push(source ? renderCitationMarker(marker, source, `citation-${key}`) : match[0]);
    } else if (match[3]) {
      nodes.push(
        <strong key={`strong-${key}`} className="answer-strong">
          {renderInlineText(match[3], sourceLookup, `strong-${key}`)}
        </strong>,
      );
    } else if (match[4]) {
      nodes.push(
        <mark key={`highlight-${key}`} className="answer-highlight">
          {renderInlineText(match[4], sourceLookup, `highlight-${key}`)}
        </mark>,
      );
    } else if (match[5]) {
      nodes.push(
        <code key={`code-${key}`} className="answer-inline-code">
          {match[5]}
        </code>,
      );
    } else if (match[6] || match[7]) {
      nodes.push(
        <em key={`em-${key}`} className="answer-emphasis">
          {renderInlineText(match[6] ?? match[7], sourceLookup, `em-${key}`)}
        </em>,
      );
    }

    cursor = matchIndex + match[0].length;
  }

  if (cursor < text.length) {
    nodes.push(text.slice(cursor));
  }

  return nodes;
}

function renderInlineContent(
  text: string,
  sourceLookup: Map<number, DisplaySource>,
  keyPrefix: string,
): React.ReactNode[] {
  return text.split("\n").flatMap((line, index) => {
    const nodes = renderInlineText(line, sourceLookup, `${keyPrefix}-line-${index}`);
    if (index === 0) {
      return nodes;
    }

    return [<br key={`${keyPrefix}-break-${index}`} />, ...nodes];
  });
}

function FormattedAnswer({
  answer,
  sourceLookup,
}: {
  answer: string;
  sourceLookup: Map<number, DisplaySource>;
}) {
  const blocks = parseAnswerBlocks(answer);

  if (blocks.length === 0) {
    return null;
  }

  return (
    <>
      {blocks.map((block, index) => {
        const key = `answer-block-${index}`;
        if (block.type === "paragraph") {
          return (
            <p key={key} className="answer-paragraph">
              {renderInlineContent(block.text, sourceLookup, key)}
            </p>
          );
        }

        if (block.type === "heading") {
          return (
            <h3 key={key} className={`answer-heading answer-heading--level-${block.level}`}>
              {renderInlineContent(block.text, sourceLookup, key)}
            </h3>
          );
        }

        if (block.type === "code") {
          return (
            <pre key={key} className="answer-code-block">
              <code>{block.code}</code>
            </pre>
          );
        }

        const ListElement = block.type === "ordered-list" ? "ol" : "ul";
        return (
          <ListElement key={key} className={`answer-list answer-list--${block.type}`}>
            {block.items.map((item, itemIndex) => (
              <li key={`${key}-item-${itemIndex}`}>
                {renderInlineContent(item, sourceLookup, `${key}-item-${itemIndex}`)}
              </li>
            ))}
          </ListElement>
        );
      })}
    </>
  );
}

function stripRenderedSourceFooters(answer: string, hasSources: boolean): string {
  if (!hasSources) {
    return answer;
  }

  return answer
    .replace(trustedWebFooterPattern, "")
    .replace(technicalSourceFooterPattern, "")
    .trimEnd();
}

function buildSourceGroups(sources: DisplaySource[]): SourceGroup[] {
  const grouped = new Map<SourceKind, DisplaySource[]>();
  for (const source of sources) {
    const existing = grouped.get(source.kind) ?? [];
    existing.push(source);
    grouped.set(source.kind, existing);
  }

  return Array.from(grouped.entries())
    .map(([key, groupedSources]) => ({
      key,
      label: sourceKindLabels[key],
      sources: groupedSources,
    }))
    .sort((left, right) => sourceKindOrder.indexOf(left.key) - sourceKindOrder.indexOf(right.key));
}

function buildGroupTitle(group: SourceGroup): string {
  const preview = group.sources
    .slice(0, 8)
    .map((source) => {
      const marker = source.marker ? `[${source.marker}] ` : "";
      return `${marker}${source.title}`;
    })
    .join("\n");
  const remaining = group.sources.length > 8 ? `\n+${group.sources.length - 8} more` : "";
  return `${group.label} sources\n${preview}${remaining}`;
}

function SourcePills({ sources }: { sources: DisplaySource[] }) {
  if (sources.length === 0) {
    return null;
  }

  const groups = buildSourceGroups(sources);

  return (
    <div className="answer-source-pills" aria-label="Cited source groups">
      {groups.map((group) => {
        const onlySource = group.sources.length === 1 ? group.sources[0] : null;
        const label = `${group.label}: ${group.sources.length} ${group.sources.length === 1 ? "source" : "sources"}`;
        const content = (
          <>
            <span className="answer-source-pill-title">{group.label}</span>
            <span className="answer-source-pill-count">{group.sources.length}</span>
          </>
        );

        return onlySource?.url ? (
          <a
            key={group.key}
            className={`answer-source-pill answer-source-pill--${group.key}`}
            href={onlySource.url}
            target="_blank"
            rel="noreferrer"
            title={buildGroupTitle(group)}
            aria-label={`Open cited source group ${label}`}
          >
            {content}
          </a>
        ) : (
          <span
            key={group.key}
            className={`answer-source-pill answer-source-pill--${group.key}`}
            title={buildGroupTitle(group)}
            aria-label={label}
          >
            {content}
          </span>
        );
      })}
    </div>
  );
}

export function CitedAnswer({ answer, citations = [], trace, className }: CitedAnswerProps) {
  const sources = buildDisplaySources(citations, trace);
  const displayAnswer = stripRenderedSourceFooters(answer, sources.length > 0);
  const sourceLookup = buildSourceLookup(sources);

  return (
    <div className={className}>
      <div className="answer-body">
        <FormattedAnswer answer={displayAnswer} sourceLookup={sourceLookup} />
      </div>
      <SourcePills sources={sources} />
    </div>
  );
}

export function CitationSources({ citations = [], trace, compact = false }: CitationSourcesProps) {
  const sources = buildDisplaySources(citations, trace);
  if (sources.length === 0) {
    return null;
  }

  return (
    <div className={compact ? "answer-source-panel answer-source-panel--compact" : "answer-source-panel"}>
      <div className="answer-source-title">Sources</div>
      <SourcePills sources={sources} />
      {buildSourceGroups(sources).map((group) => (
        <section key={group.key} className="answer-source-group" aria-label={`${group.label} sources`}>
          <div className="answer-source-group-heading">
            <span>{group.label}</span>
            <span>{group.sources.length}</span>
          </div>
          <ol className="answer-source-list">
            {group.sources.map((source) => (
              <li key={source.key} className="answer-source-card">
                <div className="answer-source-card-heading">
                  {source.marker ? <span className="answer-source-marker">[{source.marker}]</span> : null}
                  {source.url ? (
                    <a href={source.url} target="_blank" rel="noreferrer">
                      {source.title}
                    </a>
                  ) : (
                    <span>{source.title}</span>
                  )}
                </div>
                {source.excerpt ? <p className="answer-source-excerpt">{source.excerpt}</p> : null}
                <div className="answer-source-meta">
                  {source.sourceId ? <span>{source.sourceId}</span> : null}
                  {source.contentSha256 ? <span>sha256:{source.contentSha256.slice(0, 12)}</span> : null}
                </div>
              </li>
            ))}
          </ol>
        </section>
      ))}
    </div>
  );
}
