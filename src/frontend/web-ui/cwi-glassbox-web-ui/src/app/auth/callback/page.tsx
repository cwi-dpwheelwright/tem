"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { handleLoginCallback } from "@/lib/auth/oidc";

export default function AuthCallbackPage() {
  const router = useRouter();
  const [message, setMessage] = useState("Completing sign-in...");

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
    <div className="flex min-h-screen items-center justify-center px-6 text-slate-700">
      <div className="rounded-2xl bg-white/80 px-6 py-4 text-sm shadow-sm">
        {message}
      </div>
    </div>
  );
}
