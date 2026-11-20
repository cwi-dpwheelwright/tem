import { NextResponse } from "next/server";
import { resolveGatewayBaseUrl } from "@/app/api/_shared/gateway";
import { getRuntimeConfigStatus, normalizeUrl } from "@/lib/config";

interface LoginRequestBody {
  tenant_id?: string;
  username?: string;
  email?: string;
  password?: string;
}

export async function POST(request: Request) {
  let body: LoginRequestBody | null = null;
  try {
    body = (await request.json()) as LoginRequestBody;
  } catch {
    return NextResponse.json({ error: "invalid_request", message: "Invalid JSON body." }, { status: 400 });
  }

  if (!body) {
    return NextResponse.json({ error: "invalid_request", message: "Request body is required." }, { status: 400 });
  }

  const { tenant_id, username, email, password } = body;
  if (!password || !password.trim()) {
    return NextResponse.json({ error: "invalid_request", message: "Password is required." }, { status: 400 });
  }
  if ((!username || !username.trim()) && (!email || !email.trim())) {
    return NextResponse.json(
      { error: "invalid_request", message: "Username, email, or phone is required." },
      { status: 400 },
    );
  }
  const runtime = getRuntimeConfigStatus();
  const gatewayBaseUrl = resolveGatewayBaseUrl() ?? runtime.config.gatewayBaseUrl;
  if (!gatewayBaseUrl) {
    return NextResponse.json(
      { error: "config_missing", message: "Gateway base URL is not configured." },
      { status: 500 },
    );
  }

  const loginUrl = `${normalizeUrl(gatewayBaseUrl)}/auth/login`;
  const normalizedUsername = username?.trim();
  const usernameForLogin = normalizedUsername
    ? normalizePhoneNumber(normalizedUsername) ?? normalizedUsername
    : undefined;
  const tenantId = tenant_id?.trim() || undefined;

  try {
    const upstream = await fetch(loginUrl, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      body: JSON.stringify({
        tenant_id: tenantId,
        username: usernameForLogin,
        email: email?.trim() || undefined,
        password,
      }),
    });

    const payloadText = await upstream.text();
    const payload = payloadText ? safeJson(payloadText) : null;

    if (!upstream.ok) {
      const message = payload?.message || payload?.error || "Login failed.";
      return NextResponse.json(
        { error: "login_failed", message },
        { status: upstream.status },
      );
    }

    return NextResponse.json(payload ?? {}, { status: 200 });
  } catch (err) {
    const message = err instanceof Error ? err.message : "Login failed.";
    return NextResponse.json({ error: "login_failed", message }, { status: 502 });
  }
}

function safeJson(raw: string): Record<string, unknown> | null {
  try {
    return JSON.parse(raw) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function normalizePhoneNumber(value: string): string | null {
  const trimmed = value.trim();
  const digits = trimmed.replace(/\D/g, "");
  if (trimmed.startsWith("+") && digits.length >= 10 && digits.length <= 15) {
    return `+${digits}`;
  }
  if (digits.length === 10) {
    return `+1${digits}`;
  }
  if (digits.length >= 11 && digits.length <= 15 && digits.startsWith("1")) {
    return `+${digits}`;
  }
  return null;
}
