"use client";

import { useEffect, useState } from "react";
import { resolveAuthToken } from "@/lib/auth";
import type { JourneyEvent } from "@/lib/api/types";
import type { AuthMode } from "@/lib/config";
import { fetchJourneyEvents, streamJourneyEvents } from "@/lib/journey";

interface JourneyPanelProps {
  requestId?: string | null;
  seedEvents?: JourneyEvent[];
  live?: boolean;
  defaultExpanded?: boolean;
  authMode: AuthMode;
  authIdleTimeoutMs: number;
}

function sortAndDeduplicate(events: JourneyEvent[]): JourneyEvent[] {
  const byId = new Map<string, JourneyEvent>();
  for (const event of events) {
    byId.set(event.event_id, {
      ...event,
      metadata: event.metadata ?? {},
    });
  }

  return Array.from(byId.values()).sort((left, right) => {
    const leftTime = new Date(left.created_utc).getTime();
    const rightTime = new Date(right.created_utc).getTime();
    if (Number.isFinite(leftTime) && Number.isFinite(rightTime) && leftTime !== rightTime) {
      return leftTime - rightTime;
    }
    return left.event_id.localeCompare(right.event_id);
  });
}

function mergeEvents(current: JourneyEvent[], incoming: JourneyEvent[]): JourneyEvent[] {
  return sortAndDeduplicate([...current, ...incoming]);
}

function formatPhase(phase: string): string {
  return phase
    .split("_")
    .filter(Boolean)
    .map((part) => part[0]?.toUpperCase() + part.slice(1))
    .join(" ");
}

function formatTimestamp(value: string): string {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }
  return parsed.toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function describeRoute(event: JourneyEvent): string | null {
  if (event.from_component && event.to_component) {
    return `${event.from_component} -> ${event.to_component}`;
  }
  if (event.to_component) {
    return `to ${event.to_component}`;
  }
  if (event.from_component) {
    return `from ${event.from_component}`;
  }
  return null;
}

function readMetadataString(event: JourneyEvent, ...keys: string[]): string | null {
  const metadata = event.metadata ?? {};
  for (const key of keys) {
    const value = metadata[key];
    if (typeof value === "string" && value.trim().length > 0) {
      return value.trim();
    }
  }
  return null;
}

export function JourneyPanel({
  requestId,
  seedEvents,
  live = false,
  defaultExpanded = false,
  authMode,
  authIdleTimeoutMs,
}: JourneyPanelProps) {
  const [expanded, setExpanded] = useState(defaultExpanded);
  const [events, setEvents] = useState<JourneyEvent[]>(() => sortAndDeduplicate(seedEvents ?? []));
  const [isLoading, setIsLoading] = useState(false);
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setExpanded(defaultExpanded);
  }, [defaultExpanded, requestId]);

  useEffect(() => {
    setEvents(sortAndDeduplicate(seedEvents ?? []));
  }, [requestId, seedEvents]);

  useEffect(() => {
    const activeRequestId = requestId;
    if (!activeRequestId) {
      setIsLoading(false);
      setIsStreaming(false);
      setError(null);
      return;
    }
    const resolvedRequestId: string = activeRequestId;

    let active = true;
    const abortController = new AbortController();

    async function run() {
      setError(null);
      setIsLoading(true);

      try {
        const token =
          authMode === "none"
            ? null
            : await resolveAuthToken(authMode, authIdleTimeoutMs);

        if (!active) {
          return;
        }

        if (authMode !== "none" && !token) {
          setError("Sign in again to view the live path.");
          return;
        }

        const initialEvents = await fetchJourneyEvents(resolvedRequestId, token);
        if (!active) {
          return;
        }
        setEvents((current) => mergeEvents(current, initialEvents));
        setIsLoading(false);

        if (!live) {
          return;
        }

        setIsStreaming(true);
        await streamJourneyEvents(
          resolvedRequestId,
          token,
          (event) => {
            if (!active) {
              return;
            }
            setEvents((current) => mergeEvents(current, [event]));
          },
          abortController.signal,
        );
      } catch (streamError) {
        if (!active || abortController.signal.aborted) {
          return;
        }
        const message =
          streamError instanceof Error
            ? streamError.message
            : "Journey stream unavailable.";
        setError(message);
      } finally {
        if (!active) {
          return;
        }
        setIsLoading(false);
        setIsStreaming(false);
      }
    }

    void run();

    return () => {
      active = false;
      abortController.abort();
    };
  }, [authIdleTimeoutMs, authMode, live, requestId]);

  if (!requestId && events.length === 0) {
    return null;
  }

  const badgeLabel = isStreaming
    ? "Live"
    : isLoading
      ? "Syncing"
      : error
        ? "Degraded"
        : events.length > 0
          ? "Recorded"
          : "Waiting";

  return (
    <div className="journey-panel">
      <div className="journey-panel-header">
        <button
          type="button"
          className="journey-panel-toggle"
          onClick={() => setExpanded((current) => !current)}
        >
          {expanded ? "Hide transaction path" : "Show transaction path"}
        </button>
        <div className="journey-panel-status">
          <span className="journey-panel-badge">{badgeLabel}</span>
          <span>{requestId ? `Request ${requestId}` : "Awaiting request id"}</span>
        </div>
      </div>

      {expanded ? (
        <div className="journey-panel-body">
          {error ? <div className="journey-panel-empty">{error}</div> : null}
          {!error && events.length === 0 ? (
            <div className="journey-panel-empty">
              {isLoading || isStreaming ? "Listening for transaction events..." : "No transaction events recorded yet."}
            </div>
          ) : null}

          {events.length > 0 ? (
            <div className="journey-panel-list">
              {events.map((event) => {
                const route = describeRoute(event);
                const homeNodeId = readMetadataString(event, "home_node_id", "primary_home_node_id");
                return (
                  <div key={event.event_id} className="journey-panel-item">
                    <div className="journey-panel-item-header">
                      <span className="journey-panel-component">{event.component}</span>
                      <span className="journey-panel-phase">{formatPhase(event.phase)}</span>
                      {homeNodeId ? (
                        <span className="journey-panel-chip">Node {homeNodeId}</span>
                      ) : null}
                    </div>
                    <div className="journey-panel-meta">
                      <span>{formatTimestamp(event.created_utc)}</span>
                      {event.direction ? <span>{event.direction}</span> : null}
                      {route ? <span>{route}</span> : null}
                      {typeof event.status_code === "number" ? <span>HTTP {event.status_code}</span> : null}
                      {typeof event.duration_ms === "number" ? <span>{event.duration_ms} ms</span> : null}
                    </div>
                    {event.message ? <div className="journey-panel-message">{event.message}</div> : null}
                  </div>
                );
              })}
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
