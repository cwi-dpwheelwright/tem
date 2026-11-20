import { NextResponse } from "next/server";
import { resolveGatewayBaseUrl } from "@/app/api/_shared/gateway";
import { getRuntimeConfigStatus, normalizeUrl } from "@/lib/config";

export function resolveRegisterEndpoint(path: string): string | NextResponse {
  const runtime = getRuntimeConfigStatus();
  const gatewayBaseUrl = resolveGatewayBaseUrl() ?? runtime.config.gatewayBaseUrl;
  if (!gatewayBaseUrl) {
    return NextResponse.json(
      { error: "config_missing", message: "Gateway base URL is not configured." },
      { status: 500 },
    );
  }

  return `${normalizeUrl(gatewayBaseUrl)}${path}`;
}

export function resolveDefaultTenantId(): string | NextResponse {
  const tenantId = getRuntimeConfigStatus().config.tenantId;
  if (!tenantId) {
    return NextResponse.json(
      { error: "config_missing", message: "Default tenant is not configured." },
      { status: 500 },
    );
  }

  return tenantId.trim();
}

export function normalizeEmail(value: string): string | null {
  const trimmed = value.trim();
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed) ? trimmed.toLowerCase() : null;
}

export function normalizePhoneNumber(value: string): string | null {
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

export function safeJson(raw: string): Record<string, unknown> | null {
  try {
    return JSON.parse(raw) as Record<string, unknown>;
  } catch {
    return null;
  }
}
