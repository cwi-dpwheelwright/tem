import { NextResponse } from "next/server";
import { buildProxyHeaders, resolveGatewayBaseUrl } from "@/app/api/_shared/gateway";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ requestId: string }> },
) {
  const gatewayBaseUrl = resolveGatewayBaseUrl();
  if (!gatewayBaseUrl) {
    return NextResponse.json(
      { error: "config_missing", message: "Gateway base URL is not configured." },
      { status: 500 },
    );
  }

  const { requestId } = await params;
  const upstream = await fetch(`${gatewayBaseUrl}/api/journey/requests/${encodeURIComponent(requestId)}/stream`, {
    method: "GET",
    headers: buildProxyHeaders(request, "GET"),
    cache: "no-store",
  });

  const headers = new Headers();
  headers.set("content-type", upstream.headers.get("content-type") ?? "text/event-stream");
  headers.set("cache-control", "no-cache");
  headers.set("connection", "keep-alive");

  return new NextResponse(upstream.body, {
    status: upstream.status,
    headers,
  });
}
