import type { JourneyEvent, JourneyListResponse } from "./api/types";

function normalizeEvent(event: JourneyEvent): JourneyEvent {
  return {
    ...event,
    metadata: event.metadata ?? {},
  };
}

export function buildLocalJourneyEvent(
  requestId: string,
  component: string,
  phase: string,
  message?: string,
): JourneyEvent {
  return {
    event_id: `${component}-${phase}-${Date.now()}`,
    request_id: requestId,
    service: "cwi-gpt-web-ui",
    component,
    phase,
    direction: phase.includes("response") ? "response" : "request",
    message,
    created_utc: new Date().toISOString(),
    metadata: {},
  };
}

export async function fetchJourneyEvents(
  requestId: string,
  token?: string | null,
): Promise<JourneyEvent[]> {
  const headers = new Headers({ Accept: "application/json" });
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const response = await fetch(`/api/journey/requests/${encodeURIComponent(requestId)}`, {
    method: "GET",
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Journey request failed with status ${response.status}`);
  }

  const payload = (await response.json()) as JourneyListResponse;
  return (payload.items ?? []).map(normalizeEvent);
}

export async function streamJourneyEvents(
  requestId: string,
  token: string | null | undefined,
  onEvent: (event: JourneyEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  const headers = new Headers({ Accept: "text/event-stream" });
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const response = await fetch(`/api/journey/requests/${encodeURIComponent(requestId)}/stream`, {
    method: "GET",
    headers,
    cache: "no-store",
    signal,
  });

  if (!response.ok || !response.body) {
    throw new Error(`Journey stream failed with status ${response.status}`);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (!signal.aborted) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    let boundary = buffer.indexOf("\n\n");
    while (boundary >= 0) {
      const chunk = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      const dataLines = chunk
        .split("\n")
        .filter((line) => line.startsWith("data:"))
        .map((line) => line.slice(5).trim());

      if (dataLines.length > 0) {
        try {
          const payload = JSON.parse(dataLines.join("\n")) as JourneyEvent;
          onEvent(normalizeEvent(payload));
        } catch {
        }
      }

      boundary = buffer.indexOf("\n\n");
    }
  }
}
