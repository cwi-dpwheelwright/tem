"use client";

import { useEffect, useMemo, useState } from "react";
import AppShell from "@/components/app-shell";
import { createApiClient } from "@/lib/api/client";
import type { LinkStateResponse, MeshEventResponse, RoutingHintResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/context";
import { getRuntimeConfigStatus } from "@/lib/config";
import type { MeshNode, PlacementRecommendation } from "@/lib/mesh/data";
import { anonymizeId } from "@/lib/mesh/privacy";
import { useTenant } from "@/lib/tenant/context";
import { useTelemetry } from "@/lib/telemetry/context";

const HEALTH_STYLE: Record<string, string> = {
  healthy: "bg-emerald-50 text-emerald-700 border-emerald-200",
  degraded: "bg-amber-50 text-amber-700 border-amber-200",
  critical: "bg-rose-50 text-rose-700 border-rose-200",
};

function inferBand(value: string): MeshNode["band"] {
  const normalized = value.toLowerCase();
  if (normalized.includes("mmwave")) {
    return "mmwave";
  }
  if (normalized.includes("wifi")) {
    return "wifi";
  }
  return "sub6";
}

function computeLoad(link: LinkStateResponse): number {
  if (link.bandwidth_kbps && link.throughput_kbps) {
    const ratio = link.throughput_kbps / link.bandwidth_kbps;
    return Math.max(0, Math.min(1, ratio));
  }
  if (typeof link.signal_quality === "number") {
    return Math.max(0, Math.min(1, 1 - link.signal_quality));
  }
  return 0.5;
}

function toRisk(confidence: number): PlacementRecommendation["risk"] {
  if (confidence < 0.5) {
    return "high";
  }
  if (confidence < 0.75) {
    return "medium";
  }
  return "low";
}

export default function MeshPage() {
  const { trackEvent } = useTelemetry();
  const { accessToken } = useAuth();
  const { tenantId } = useTenant();
  const config = useMemo(() => getRuntimeConfigStatus(), []);
  const [band, setBand] = useState("all");
  const [health, setHealth] = useState("all");
  const [privacyMode, setPrivacyMode] = useState(false);
  const [nodes, setNodes] = useState<MeshNode[]>([]);
  const [recommendations, setRecommendations] = useState<PlacementRecommendation[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    trackEvent("page_view", "mesh", { page: "mesh" });
  }, [trackEvent]);

  useEffect(() => {
    if (!config.config?.apiBaseUrl) {
      setError("Missing API base URL configuration.");
      setIsLoading(false);
      return;
    }

    const client = createApiClient({
      baseUrl: config.config?.apiBaseUrl ?? "",
      tokenProvider: () => accessToken,
      tenantIdProvider: () => tenantId,
    });

    const fetchData = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const [linkStates, meshEvents] = await Promise.all([
          client.meshLinks(tenantId),
          client.meshEvents(20),
        ]);
        setNodes(buildNodes(linkStates));
        const hints = await buildRecommendations(client, linkStates, meshEvents);
        setRecommendations(hints);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Unable to load mesh data.");
      } finally {
        setIsLoading(false);
      }
    };

    void fetchData();
  }, [accessToken, config.config?.apiBaseUrl, tenantId]);

  const filteredNodes = useMemo(() => {
    return nodes.filter((node) => {
      const matchesBand = band === "all" || node.band === band;
      const matchesHealth = health === "all" || node.health === health;
      return matchesBand && matchesHealth;
    });
  }, [band, health, nodes]);

  const nodeLabel = (node: MeshNode) => (privacyMode ? anonymizeId(node.id) : node.label);

  const coords = useMemo(() => {
    const map = new Map<string, { x: number; y: number }>();
    const columns = filteredNodes.length <= 3 ? filteredNodes.length || 1 : 3;
    const spacingX = 520 / (columns + 1);
    const spacingY = 220 / (Math.ceil(filteredNodes.length / columns) + 1);
    filteredNodes.forEach((node, index) => {
      const col = index % columns;
      const row = Math.floor(index / columns);
      map.set(node.id, {
        x: Math.round(spacingX * (col + 1)),
        y: Math.round(spacingY * (row + 1)),
      });
    });
    return map;
  }, [filteredNodes]);

  const topologyLinks = useMemo(() => {
    const list: { from: string; to: string; quality: number }[] = [];
    for (let i = 0; i < filteredNodes.length - 1; i += 1) {
      list.push({
        from: filteredNodes[i].id,
        to: filteredNodes[i + 1].id,
        quality: Math.min(filteredNodes[i].load, filteredNodes[i + 1].load),
      });
    }
    return list;
  }, [filteredNodes]);

  function exportRecommendations() {
    const payload = {
      generated_at: new Date().toISOString(),
      recommendations,
    };
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = "placement-recommendations.json";
    anchor.click();
    URL.revokeObjectURL(url);
    trackEvent("placement_export", "mesh", { count: recommendations.length });
  }

  return (
    <AppShell
      title="Mesh status + placement"
      subtitle="Review topology health, enable privacy masking, and action placement recommendations."
      badge="Mesh"
    >
      <div className="grid gap-6 lg:grid-cols-[1.1fr_0.9fr]">
        <section className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <p className="text-sm font-semibold text-slate-800">Topology overview</p>
                <p className="mt-1 text-xs text-slate-600">
                  Filter by band and health, or toggle privacy mode to anonymize node IDs.
                </p>
              </div>
              <button
                className={`rounded-full px-3 py-1 text-xs font-semibold uppercase tracking-[0.2em] ${
                  privacyMode
                    ? "bg-slate-900 text-white"
                    : "border border-slate-200 bg-white text-slate-600"
                }`}
                onClick={() => {
                  setPrivacyMode((prev) => !prev);
                  trackEvent("privacy_toggle", "mesh", { enabled: !privacyMode });
                }}
              >
                {privacyMode ? "Privacy on" : "Privacy off"}
              </button>
            </div>

            <div className="mt-4 flex flex-wrap gap-2 text-xs">
              {["all", "sub6", "mmwave", "wifi"].map((item) => (
                <button
                  key={item}
                  className={`rounded-full border px-3 py-1 uppercase tracking-[0.2em] ${
                    band === item
                      ? "border-slate-900 bg-slate-900 text-white"
                      : "border-slate-200 bg-white text-slate-600"
                  }`}
                  onClick={() => {
                    setBand(item);
                    trackEvent("filter_band", "mesh", { band: item });
                  }}
                >
                  {item}
                </button>
              ))}
            </div>

            <div className="mt-3 flex flex-wrap gap-2 text-xs">
              {["all", "healthy", "degraded", "critical"].map((item) => (
                <button
                  key={item}
                  className={`rounded-full border px-3 py-1 uppercase tracking-[0.2em] ${
                    health === item
                      ? "border-slate-900 bg-slate-900 text-white"
                      : "border-slate-200 bg-white text-slate-600"
                  }`}
                  onClick={() => {
                    setHealth(item);
                    trackEvent("filter_health", "mesh", { health: item });
                  }}
                >
                  {item}
                </button>
              ))}
            </div>

            <div className="mt-5 rounded-2xl border border-slate-200 bg-white p-4">
              <svg viewBox="0 0 520 220" className="h-52 w-full">
                {topologyLinks.map((link) => {
                  const from = coords.get(link.from);
                  const to = coords.get(link.to);
                  if (!from || !to) {
                    return null;
                  }
                  return (
                    <line
                      key={`${link.from}-${link.to}`}
                      x1={from.x}
                      y1={from.y}
                      x2={to.x}
                      y2={to.y}
                      stroke="#cbd5f5"
                      strokeWidth="2"
                      opacity={Math.max(0.2, link.quality)}
                    />
                  );
                })}
                {filteredNodes.map((node) => {
                  const position = coords.get(node.id) ?? { x: 0, y: 0 };
                  return (
                    <g key={node.id}>
                      <circle cx={position.x} cy={position.y} r="20" fill="#0f172a" />
                      <text x={position.x} y={position.y + 4} textAnchor="middle" fill="#fff" fontSize="8">
                        {nodeLabel(node)}
                      </text>
                    </g>
                  );
                })}
              </svg>
            </div>

            {isLoading ? (
              <div className="mt-4 rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-6 text-sm text-slate-500">
                Loading mesh topology...
              </div>
            ) : null}
            {error ? (
              <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
                {error}
              </div>
            ) : null}

            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              {filteredNodes.map((node) => (
                <div key={node.id} className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-slate-900">{nodeLabel(node)}</p>
                    <span
                      className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-[0.2em] ${
                        HEALTH_STYLE[node.health]
                      }`}
                    >
                      {node.health}
                    </span>
                  </div>
                  <p className="mt-1 text-xs text-slate-500">Band: {node.band}</p>
                  <p className="mt-1 text-xs text-slate-500">Interface: {node.location}</p>
                  <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-slate-100">
                    <div className="h-full bg-amber-500" style={{ width: `${node.load * 100}%` }} />
                  </div>
                  <p className="mt-1 text-xs text-slate-500">Load {(node.load * 100).toFixed(0)}%</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <aside className="space-y-6">
          <div className="rounded-3xl border border-white/80 bg-white/80 p-6 shadow-lg shadow-slate-200/60">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-semibold text-slate-800">Placement recommendations</p>
                <p className="mt-1 text-xs text-slate-600">
                  Suggested adjustments based on multi-band mesh assessment.
                </p>
              </div>
              <button
                className="rounded-2xl border border-slate-200 bg-white px-3 py-1 text-xs font-semibold text-slate-700"
                onClick={exportRecommendations}
                disabled={recommendations.length === 0}
              >
                Export
              </button>
            </div>
            <div className="mt-4 space-y-3">
              {recommendations.map((rec) => (
                <div key={rec.id} className="rounded-2xl border border-slate-200 bg-white px-4 py-3">
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-slate-900">{rec.target}</p>
                    <span
                      className={`rounded-full border px-2 py-1 text-[10px] uppercase tracking-[0.2em] ${
                        rec.risk === "high"
                          ? "border-rose-200 bg-rose-50 text-rose-700"
                          : rec.risk === "medium"
                          ? "border-amber-200 bg-amber-50 text-amber-700"
                          : "border-emerald-200 bg-emerald-50 text-emerald-700"
                      }`}
                    >
                      {rec.risk}
                    </span>
                  </div>
                  <p className="mt-2 text-xs text-slate-600">{rec.suggestion}</p>
                  <p className="mt-2 text-xs text-slate-500">Impact: {rec.impact}</p>
                </div>
              ))}
              {recommendations.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-slate-200 bg-white px-4 py-6 text-sm text-slate-500">
                  No placement recommendations available.
                </div>
              ) : null}
            </div>
          </div>
        </aside>
      </div>
    </AppShell>
  );
}

function buildNodes(linkStates: LinkStateResponse[]): MeshNode[] {
  const map = new Map<
    string,
    { statuses: string[]; loadSum: number; count: number; interfaceName: string }
  >();

  linkStates.forEach((link) => {
    const entry = map.get(link.node_id) ?? {
      statuses: [],
      loadSum: 0,
      count: 0,
      interfaceName: link.interface,
    };
    entry.statuses.push(link.status);
    entry.loadSum += computeLoad(link);
    entry.count += 1;
    entry.interfaceName = link.interface;
    map.set(link.node_id, entry);
  });

  return Array.from(map.entries()).map(([nodeId, entry]) => {
    const health = entry.statuses.includes("down")
      ? "critical"
      : entry.statuses.includes("degraded")
      ? "degraded"
      : "healthy";
    return {
      id: nodeId,
      label: nodeId,
      band: inferBand(entry.interfaceName),
      health,
      location: entry.interfaceName || "unknown",
      load: entry.count > 0 ? entry.loadSum / entry.count : 0.5,
    };
  });
}

async function buildRecommendations(
  client: ReturnType<typeof createApiClient>,
  linkStates: LinkStateResponse[],
  events: MeshEventResponse[],
): Promise<PlacementRecommendation[]> {
  const nodes = buildNodes(linkStates);
  const candidates = [...nodes].sort((a, b) => b.load - a.load).slice(0, 3);
  const recs: PlacementRecommendation[] = [];

  for (const node of candidates) {
    const complexity = node.health === "critical" ? "high" : node.health === "degraded" ? "medium" : "low";
    let hint: RoutingHintResponse | null = null;
    try {
      hint = await client.meshHint({ node_id: node.id, task_complexity: complexity });
    } catch {
      hint = null;
    }

    const event = events.find((evt) => evt.node_id === node.id);
    const confidence = hint?.confidence ?? 0.4;
    recs.push({
      id: `rec-${node.id}`,
      target: node.label,
      suggestion: hint
        ? `Route via ${hint.preferred_interface}. ${hint.reason}`
        : `Review ${node.label} for routing adjustments. ${event?.reason ?? "No hint available."}`,
      impact: hint ? `Confidence ${(confidence * 100).toFixed(0)}%` : "Manual review",
      risk: toRisk(confidence),
    });
  }

  return recs;
}
