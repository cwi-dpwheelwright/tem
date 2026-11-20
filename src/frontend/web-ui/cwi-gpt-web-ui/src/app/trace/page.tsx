"use client";

import { useRouter } from "next/navigation";
import { TraceLookupPanel } from "@/components/trace-lookup-panel";

export default function TraceHomePage() {
  const router = useRouter();

  return (
    <TraceLookupPanel
      onLookup={(traceId) => router.push(`/trace/${encodeURIComponent(traceId)}`)}
    />
  );
}
