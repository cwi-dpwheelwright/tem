import { NextResponse } from "next/server";
import { resolveRegisterEndpoint, safeJson } from "@/app/api/register/_shared";

interface RegisterConfirmRequestBody {
  verification_id?: string;
  code?: string;
  password?: string;
}

export async function POST(request: Request) {
  let body: RegisterConfirmRequestBody | null = null;
  try {
    body = (await request.json()) as RegisterConfirmRequestBody;
  } catch {
    return NextResponse.json({ error: "invalid_request", message: "Invalid JSON body." }, { status: 400 });
  }

  const verificationId = body?.verification_id?.trim();
  const code = body?.code?.trim();
  const password = body?.password;
  if (!verificationId) {
    return NextResponse.json({ error: "invalid_request", message: "Verification ID is required." }, { status: 400 });
  }
  if (!code) {
    return NextResponse.json({ error: "invalid_request", message: "Verification code is required." }, { status: 400 });
  }
  if (!password || !password.trim()) {
    return NextResponse.json({ error: "invalid_request", message: "Password is required." }, { status: 400 });
  }

  const registerUrl = resolveRegisterEndpoint("/auth/register/confirm");
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
        verification_id: verificationId,
        code,
        password,
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
