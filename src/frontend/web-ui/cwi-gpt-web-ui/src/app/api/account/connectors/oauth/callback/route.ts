import { NextRequest, NextResponse } from "next/server";

export function GET(request: NextRequest) {
  const status = request.nextUrl.searchParams.has("error") ? "error" : "returned";
  const forwardedHost = request.headers.get("x-forwarded-host");
  const forwardedProto = request.headers.get("x-forwarded-proto");
  const host = forwardedHost || request.headers.get("host") || request.nextUrl.host;
  const isLocalHost = host.includes("localhost") || host.startsWith("127.") || host.startsWith("0.0.0.0");
  const proto = isLocalHost ? (forwardedProto || "http") : "https";
  const redirectUrl = new URL("/settings", `${proto}://${host}`);
  redirectUrl.searchParams.set("connector_auth", status);
  return NextResponse.redirect(redirectUrl);
}
