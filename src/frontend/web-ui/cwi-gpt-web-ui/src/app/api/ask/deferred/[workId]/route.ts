import { proxyGatewayRequest } from "@/app/api/_shared/gateway";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ workId: string }> },
) {
  const { workId } = await params;
  const requestUrl = new URL(request.url);
  const requestId = requestUrl.searchParams.get("request_id") ?? "";
  const query = new URLSearchParams();
  if (requestId) {
    query.set("request_id", requestId);
  }

  const queryString = query.toString();
  const upstreamPath = `/api/ask/deferred/${encodeURIComponent(workId)}${
    queryString ? `?${queryString}` : ""
  }`;
  return proxyGatewayRequest(request, upstreamPath);
}
