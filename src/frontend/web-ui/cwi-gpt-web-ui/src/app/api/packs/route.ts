import { NextResponse } from "next/server";
import { resolveGatewayBaseUrl } from "@/app/api/_shared/gateway";
import { getRuntimeConfigStatus, normalizeUrl } from "@/lib/config";

async function proxyPackRequest(url: string, request: Request) {
  const upstream = await fetch(url, {
    method: "GET",
    headers: {
      Accept: request.headers.get("accept") ?? "*/*",
    },
    cache: "no-store",
  });

  const body = await upstream.arrayBuffer();
  const headers = new Headers();
  const contentType = upstream.headers.get("content-type");
  if (contentType) {
    headers.set("content-type", contentType);
  }
  const etag = upstream.headers.get("etag");
  if (etag) {
    headers.set("etag", etag);
  }
  const cacheControl = upstream.headers.get("cache-control");
  if (cacheControl) {
    headers.set("cache-control", cacheControl);
  }
  const lastModified = upstream.headers.get("last-modified");
  if (lastModified) {
    headers.set("last-modified", lastModified);
  }

  return new NextResponse(body, { status: upstream.status, headers });
}

export async function GET(request: Request) {
  const runtime = getRuntimeConfigStatus();
  const gatewayBaseUrl = resolveGatewayBaseUrl() ?? runtime.config.gatewayBaseUrl;
  if (!gatewayBaseUrl) {
    return NextResponse.json(
      { error: "config_missing", message: "Gateway base URL is not configured." },
      { status: 500 },
    );
  }

  const url = `${normalizeUrl(gatewayBaseUrl)}/packs`;

  try {
    return await proxyPackRequest(url, request);
  } catch (err) {
    const message = err instanceof Error ? err.message : "Pack catalog fetch failed.";
    return NextResponse.json({ error: "pack_proxy_failed", message }, { status: 502 });
  }
}
