import { NextResponse } from "next/server";

function readBoolean(name: string, fallback = false): boolean {
  const value = process.env[name];
  if (!value) {
    return fallback;
  }

  if (value === "1") {
    return true;
  }

  if (value === "0") {
    return false;
  }

  return value.toLowerCase() === "true";
}

function readNumber(name: string, fallback: number): number {
  const parsed = Number(process.env[name]);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

export async function GET() {
  return NextResponse.json({
    enabled: readBoolean("CWI_TRUSTED_WEB_CONTEXT_ENABLED", false),
    default_profile: process.env.CWI_TRUSTED_WEB_CONTEXT_DEFAULT_PROFILE || "general",
    max_results: readNumber("CWI_TRUSTED_WEB_CONTEXT_MAX_RESULTS", 5),
  });
}
