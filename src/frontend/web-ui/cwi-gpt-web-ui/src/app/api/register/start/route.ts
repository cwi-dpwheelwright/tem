import { NextResponse } from "next/server";
import {
  normalizeEmail,
  normalizePhoneNumber,
  resolveDefaultTenantId,
  resolveRegisterEndpoint,
  safeJson,
} from "@/app/api/register/_shared";

interface RegisterStartRequestBody {
  display_name?: string;
  identifier?: string;
  username?: string;
}

export async function POST(request: Request) {
  let body: RegisterStartRequestBody | null = null;
  try {
    body = (await request.json()) as RegisterStartRequestBody;
  } catch {
    return NextResponse.json({ error: "invalid_request", message: "Invalid JSON body." }, { status: 400 });
  }

  const displayName = body?.display_name?.trim();
  const identifier = body?.identifier?.trim();
  const username = body?.username?.trim();
  if (!displayName) {
    return NextResponse.json({ error: "invalid_request", message: "Display name is required." }, { status: 400 });
  }
  if (!identifier) {
    return NextResponse.json({ error: "invalid_request", message: "Email or phone number is required." }, { status: 400 });
  }

  const email = normalizeEmail(identifier);
  const phone = normalizePhoneNumber(identifier);
  if (!email && !phone) {
    return NextResponse.json(
      { error: "invalid_request", message: "Enter a valid email address or phone number." },
      { status: 400 },
    );
  }

  const tenantId = resolveDefaultTenantId();
  if (tenantId instanceof NextResponse) {
    return tenantId;
  }
  const registerUrl = resolveRegisterEndpoint("/auth/register/start");
  if (registerUrl instanceof NextResponse) {
    return registerUrl;
  }

  try {
    const upstream = await fetch(registerUrl, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      body: JSON.stringify({
        tenant_id: tenantId,
        display_name: displayName,
        email: email ?? undefined,
        phone: phone ?? undefined,
        username: username || undefined,
      }),
    });

    const payloadText = await upstream.text();
    const payload = payloadText ? safeJson(payloadText) : null;
    if (!upstream.ok) {
      const message = payload?.message || payload?.error || "Registration failed.";
      return NextResponse.json({ error: "registration_failed", message }, { status: upstream.status });
    }

    return NextResponse.json(payload ?? {}, { status: 200 });
  } catch (err) {
    const message = err instanceof Error ? err.message : "Registration failed.";
    return NextResponse.json({ error: "registration_failed", message }, { status: 502 });
  }
}
