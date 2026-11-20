import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ ticketId: string }> },
) {
  const resolved = await params;
  const ticketId = resolved.ticketId ?? "";
  return proxyGatewayRequest(
    request,
    `/api/approval/${encodeURIComponent(ticketId)}`,
  );
}
