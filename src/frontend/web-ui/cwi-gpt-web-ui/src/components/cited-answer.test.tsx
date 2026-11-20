import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { CitedAnswer, CitationSources } from "@/components/cited-answer";
import type { AskCitation, Trace } from "@/lib/api/types";

describe("CitedAnswer", () => {
  afterEach(() => cleanup());

  it("renders supported inline citation markers as source links", () => {
    const citations: AskCitation[] = [
      {
        doc_id: "web:trusted-market",
        chunk_id: "news-current-market-update",
        marker: 1,
        source_id: "web:trusted-market:news-current-market-update",
        title: "Current Market Update",
        url: "https://trusted.example/news/current-market-update",
      },
    ];

    render(<CitedAnswer answer="Backlog changed this week [1]." citations={citations} />);

    const marker = screen.getByRole("link", { name: /open source 1/i });
    expect(marker.getAttribute("href")).toBe("https://trusted.example/news/current-market-update");
    expect(screen.getByText(/Backlog changed this week/i)).toBeTruthy();
    expect(screen.getByRole("link", { name: /open cited source group web/i })).toBeTruthy();
  });

  it("ignores unsupported markers instead of creating fabricated links", () => {
    render(<CitedAnswer answer="Backlog changed this week [9]." citations={[]} />);

    expect(screen.queryByRole("link", { name: /open source 9/i })).toBeNull();
    expect(screen.getByText("Backlog changed this week [9].")).toBeTruthy();
  });

  it("renders trusted web footer sources as compact pills instead of answer prose", () => {
    const citations: AskCitation[] = [
      {
        doc_id: "web:spb",
        chunk_id: "capitol-history",
        marker: 1,
        source_id: "web:spb:capitol-history",
        title: "SPB - Capitol History",
        url: "https://www.tspb.texas.gov/prop/tc/tc-history/history/",
      },
      {
        doc_id: "web:wikipedia-reference",
        chunk_id: "austin-texas",
        marker: 2,
        source_id: "web:wikipedia-reference:austin-texas",
        title: "Austin, Texas - Wikipedia",
        url: "https://en.wikipedia.org/wiki/Austin,_Texas",
      },
    ];

    render(
      <CitedAnswer
        answer={
          "The capital of Texas is Austin.\n\nTrusted web sources consulted: [1] SPB - Capitol History; [2] Austin, Texas - Wikipedia."
        }
        citations={citations}
      />,
    );

    expect(screen.getByText("The capital of Texas is Austin.")).toBeTruthy();
    expect(screen.queryByText(/Trusted web sources consulted/i)).toBeNull();
    expect(screen.getByLabelText(/web: 2 sources/i)).toBeTruthy();
  });

  it("renders technical trace sources as grouped pills instead of raw source text", () => {
    const trace: Trace = {
      sources: [
        "memory:mem-profile-1",
        "memory:mem-project-2",
        "pack:cwi-memory-capsule@1.0.0",
        "mcp_tool:search.web",
      ],
    };

    render(
      <CitedAnswer
        answer={
          "Here is what I know from memory.\n\nConfidence: 0.91\nSources: memory:mem-profile-1; memory:mem-project-2; pack:cwi-memory-capsule@1.0.0; mcp_tool:search.web"
        }
        trace={trace}
      />,
    );

    expect(screen.getByText(/Here is what I know from memory/i)).toBeTruthy();
    expect(screen.queryByText(/Sources: memory:/i)).toBeNull();
    expect(screen.getByLabelText(/memory: 2 sources/i)).toBeTruthy();
    expect(screen.getByLabelText(/packs: 1 source/i)).toBeTruthy();
    expect(screen.getByLabelText(/tools: 1 source/i)).toBeTruthy();
  });

  it("renders presentation formatting for bold, highlights, and bullet lists", () => {
    render(
      <CitedAnswer
        answer={
          "**Current focus:** ==Project Cedar Lens==\n\n- Validate memory scope\n- Keep sources grouped"
        }
      />,
    );

    expect(screen.getByText("Current focus:").tagName).toBe("STRONG");
    expect(screen.getByText("Project Cedar Lens").tagName).toBe("MARK");
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
    expect(screen.getByText("Validate memory scope")).toBeTruthy();
  });

  it("renders ordered lists and code blocks without exposing markdown fences", () => {
    render(
      <CitedAnswer
        answer={
          "1. Normalize the input\n2. Select the response mode\n\n```ts\nconst ready = true;\n```"
        }
      />,
    );

    expect(screen.getAllByRole("listitem")).toHaveLength(2);
    expect(screen.getByText("Normalize the input")).toBeTruthy();
    expect(screen.getByText("const ready = true;").tagName).toBe("CODE");
    expect(screen.queryByText(/```/)).toBeNull();
  });

  it("keeps citations clickable inside formatted answer text", () => {
    const citations: AskCitation[] = [
      {
        doc_id: "web:texas",
        chunk_id: "capital",
        marker: 1,
        source_id: "web:texas:capital",
        title: "Texas Capitol",
        url: "https://trusted.example/texas-capitol",
      },
    ];

    render(<CitedAnswer answer="**Austin** is the capital [1]." citations={citations} />);

    expect(screen.getByText("Austin").tagName).toBe("STRONG");
    expect(screen.getByRole("link", { name: /open source 1/i }).getAttribute("href")).toBe(
      "https://trusted.example/texas-capitol",
    );
  });

  it("renders raw html-like text as text rather than markup", () => {
    render(<CitedAnswer answer={"<script>alert('x')</script>\n\n- **Safe text**"} />);

    expect(screen.getByText("<script>alert('x')</script>")).toBeTruthy();
    expect(document.querySelector("script")).toBeNull();
    expect(screen.getByText("Safe text").tagName).toBe("STRONG");
  });
});

describe("CitationSources", () => {
  afterEach(() => cleanup());

  it("renders source cards from trace trusted web sources when history has no citation array", () => {
    const trace: Trace = {
      trusted_web_sources: [
        {
          source_id: "trusted-market",
          evidence_source_id: "web:trusted-market:news-current-market-update",
          marker: 1,
          title: "Current Market Update",
          url: "https://trusted.example/news/current-market-update",
          chunk_id: "news-current-market-update",
          excerpt: "Trusted market update: backlog changed.",
          content_sha256: "a".repeat(64),
          score: 0.9,
          retrieved_utc: "2026-05-01T00:00:00Z",
        },
      ],
    };

    render(<CitationSources trace={trace} />);

    expect(screen.getByText("Current Market Update")).toBeTruthy();
    expect(screen.getByText(/Trusted market update/i)).toBeTruthy();
    expect(screen.getByText(/sha256:aaaaaaaaaaaa/i)).toBeTruthy();
  });
});
