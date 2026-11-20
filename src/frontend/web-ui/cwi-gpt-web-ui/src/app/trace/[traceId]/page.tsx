"use client";

import { useParams } from "next/navigation";
import { TraceDetailPanel } from "@/components/trace-detail-panel";

export default function TraceDetailPage() {
  const params = useParams();
  const traceId = typeof params.traceId === "string" ? params.traceId : "";

  return <TraceDetailPanel traceId={traceId} />;
}
