"use client";

import { useState } from "react";

interface TraceLookupPanelProps {
  onLookup: (traceId: string) => void;
}

export function TraceLookupPanel({ onLookup }: TraceLookupPanelProps) {
  const [traceId, setTraceId] = useState("");

  const submit = () => {
    const trimmed = traceId.trim();
    if (!trimmed) {
      return;
    }
    onLookup(trimmed);
  };

  return (
    <div className="page-stack">
      <div className="card-light">
        <h2 className="card-title">Trace lookup</h2>
        <p className="card-subtitle">
          Enter a trace ID to fetch the correlated document identifier.
        </p>
        <div className="trace-input-row">
          <input
            className="input-text"
            placeholder="trace-id"
            value={traceId}
            onChange={(event) => setTraceId(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                submit();
              }
            }}
          />
          <button
            className="btn-primary"
            onClick={submit}
          >
            Lookup
          </button>
        </div>
      </div>
    </div>
  );
}
