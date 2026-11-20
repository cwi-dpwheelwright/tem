"use client";

import { useRouter } from "next/navigation";
import { RegisterPanel } from "@/components/register-panel";

export default function RegisterPage() {
  const router = useRouter();

  return (
    <RegisterPanel
      onBackToLogin={() => router.push("/login")}
      onRegistered={() => router.replace("/")}
    />
  );
}
