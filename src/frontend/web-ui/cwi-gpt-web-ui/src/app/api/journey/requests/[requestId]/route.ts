import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ requestId: string }> },
) {
  const { requestId } = await params;
  return proxyGatewayRequest(request, `/api/journey/requests/${encodeURIComponent(requestId)}`);
}
