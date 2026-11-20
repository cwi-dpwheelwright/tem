import { getRuntimeConfigStatus } from "@/lib/config";

function getBrowserOrigin(): string | null {
  if (typeof window === "undefined") {
    return null;
  }

  const origin = window.location.origin?.trim();
  return origin ? origin : null;
}

export function resolveIdentityBaseUrl(): string | null {
  const status = getRuntimeConfigStatus();
  return status.config?.identityBaseUrl ?? getBrowserOrigin();
}
