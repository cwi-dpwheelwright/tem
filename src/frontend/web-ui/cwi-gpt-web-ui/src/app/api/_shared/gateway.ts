import { NextResponse } from "next/server";
import { getRuntimeConfigStatus } from "@/lib/config";

const REQUEST_ID_HEADERS = ["x-request-id", "x-correlation-id"];
const GATEWAY_ABORT_MESSAGE = "CWI is still working, but the gateway connection ended before a response was ready. Please retry in a moment.";

export function resolveGatewayBaseUrl(): string | null {
  return getRuntimeConfigStatus().config.gatewayBaseUrl || null;
}

function getRequestId(response: Response): string | null {
  for (const header of REQUEST_ID_HEADERS) {
    const value = response.headers.get(header);
    if (value) {
      return value;
    }
  }
  return null;
}

export function buildProxyHeaders(request: Request, method: string): Headers {
  const headers = new Headers();
  headers.set("Accept", "application/json");

  const authorization = request.headers.get("authorization");
  if (authorization) {
    headers.set("Authorization", authorization);
  }

  const roles = request.headers.get("x-roles");
  if (roles) {
    headers.set("X-Roles", roles);
  }

  const modelTier = request.headers.get("x-model-tier");
  if (modelTier) {
    headers.set("x-model-tier", modelTier);
  }

  const requestId = request.headers.get("x-request-id");
  if (requestId) {
    headers.set("x-request-id", requestId);
  }

  if (method !== "GET" && method !== "HEAD") {
    const contentType = request.headers.get("content-type");
    if (contentType) {
      headers.set("Content-Type", contentType);
    }
  }

  return headers;
}

export async function proxyGatewayRequest(
  request: Request,
  upstreamPath: string,
): Promise<NextResponse> {
  const gatewayBaseUrl = resolveGatewayBaseUrl();
  if (!gatewayBaseUrl) {
    return NextResponse.json(
      { error: "config_missing", message: "Gateway base URL is not configured." },
      { status: 500 },
    );
  }

  const method = request.method.toUpperCase();
  const url = `${gatewayBaseUrl}${upstreamPath}`;
  const headers = buildProxyHeaders(request, method);
  const body = method === "GET" || method === "HEAD" ? undefined : await request.text();

  try {
    const upstream = await fetch(url, {
      method,
      headers,
      body,
      cache: "no-store",
    });

    const responseBody = await upstream.text();
    const responseHeaders = new Headers();
    const contentType = upstream.headers.get("content-type");
    if (contentType) {
      responseHeaders.set("content-type", contentType);
    }
    const requestId = getRequestId(upstream);
    if (requestId) {
      responseHeaders.set("x-request-id", requestId);
    }

    return new NextResponse(responseBody, {
      status: upstream.status,
      headers: responseHeaders,
    });
  } catch (err) {
    const message = isAbortLikeError(err)
      ? GATEWAY_ABORT_MESSAGE
      : err instanceof Error ? err.message : "Gateway proxy failed.";
    return NextResponse.json({ error: "gateway_proxy_failed", message }, { status: 502 });
  }
}

function isAbortLikeError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  return error.name === "AbortError"
    || /aborted without reason/i.test(error.message)
    || /operation was aborted/i.test(error.message);
}
