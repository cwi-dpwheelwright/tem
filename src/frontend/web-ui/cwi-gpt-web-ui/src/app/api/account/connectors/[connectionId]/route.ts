import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function PATCH(
  request: Request,
  { params }: { params: Promise<{ connectionId: string }> },
) {
  const { connectionId } = await params;
  return proxyGatewayRequest(request, `/api/account/connectors/${encodeURIComponent(connectionId)}`);
}

export async function DELETE(
  request: Request,
  { params }: { params: Promise<{ connectionId: string }> },
) {
  const { connectionId } = await params;
  return proxyGatewayRequest(request, `/api/account/connectors/${encodeURIComponent(connectionId)}`);
}
