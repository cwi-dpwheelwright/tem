import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function POST(request: Request) {
  return proxyGatewayRequest(request, "/api/action");
}
