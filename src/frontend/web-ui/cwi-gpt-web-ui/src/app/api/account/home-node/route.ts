import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function GET(request: Request) {
  return proxyGatewayRequest(request, "/api/account/home-node");
}

export async function POST(request: Request) {
  return proxyGatewayRequest(request, "/api/account/home-node");
}

export async function DELETE(request: Request) {
  return proxyGatewayRequest(request, "/api/account/home-node");
}
