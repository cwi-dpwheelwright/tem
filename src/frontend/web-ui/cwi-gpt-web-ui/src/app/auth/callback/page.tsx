"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { handleLoginCallback } from "@/lib/oidc";
import { loadOverrides, saveOverrides } from "@/lib/storage";

export default function AuthCallbackPage() {
  const router = useRouter();
  const [message, setMessage] = useState("Completing sign-in...");

  useEffect(() => {
    handleLoginCallback()
      .then((user) => {
        const returnTo = (user?.state as { returnTo?: string } | null)?.returnTo;
        const existing = loadOverrides();
        const rest = { ...existing };
        delete rest.userId;
        saveOverrides(rest);
        router.replace(returnTo || "/");
      })
      .catch((err) => {
        setMessage(err instanceof Error ? err.message : "Login failed.");
      });
  }, [router]);

  return (
    <div className="auth-callback">
      <div className="auth-callback-card">
        {message}
      </div>
    </div>
  );
}
