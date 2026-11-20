import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ traceId: string }> },
) {
  const resolved = await params;
  const traceId = resolved.traceId ?? "";
  return proxyGatewayRequest(
    request,
    `/api/trace/${encodeURIComponent(traceId)}`,
  );
}
