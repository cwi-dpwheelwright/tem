import { NextResponse } from "next/server";

export async function GET() {
  return NextResponse.json({
    ok: true,
    ready: true,
    checks: {
      app: {
        status: "ok",
        detail: null,
      },
    },
    timestamp: new Date().toISOString(),
  });
}
