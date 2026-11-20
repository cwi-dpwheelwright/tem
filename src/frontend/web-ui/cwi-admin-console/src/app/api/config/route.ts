import { NextResponse } from "next/server";
import { getRuntimeConfigStatus } from "@/lib/config";

export function GET() {
  return NextResponse.json(getRuntimeConfigStatus());
}
