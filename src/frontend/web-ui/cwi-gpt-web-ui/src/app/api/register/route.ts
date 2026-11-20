import { NextResponse } from "next/server";

export async function POST() {
  return NextResponse.json(
    {
      error: "verification_required",
      message: "Start registration with /api/register/start and confirm with /api/register/confirm.",
    },
    { status: 409 },
  );
}
