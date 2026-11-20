"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { handleLoginCallback } from "@/lib/auth/oidc";

export default function AuthCallbackPage() {
  const router = useRouter();
  const [message, setMessage] = useState("Completing sign-in…");

  useEffect(() => {
    handleLoginCallback()
      .then((user) => {
        const returnTo = (user?.state as { returnTo?: string } | null)?.returnTo;
        router.replace(returnTo || "/");
      })
      .catch((err) => {
        setMessage(err instanceof Error ? err.message : "Login failed.");
      });
  }, [router]);

  return (
    <div className="flex min-h-screen items-center justify-center px-6 text-slate-200">
      <div className="rounded-2xl border border-white/10 bg-slate-950/60 px-6 py-4 text-sm shadow-xl shadow-black/30">
        {message}
      </div>
    </div>
  );
}
