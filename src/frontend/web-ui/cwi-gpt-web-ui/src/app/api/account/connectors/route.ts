import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function GET(request: Request) {
  return proxyGatewayRequest(request, "/api/account/connectors");
}

export async function POST(request: Request) {
  return proxyGatewayRequest(request, "/api/account/connectors");
}
