"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo } from "react";
import { LoginPanel } from "@/components/login-panel";
import { NoValidPacksModal } from "@/components/no-valid-packs-modal";
import { resolveRuntimeConfig } from "@/lib/config";
import { loadOverrides } from "@/lib/storage";
import { usePackCatalogAvailability } from "@/lib/packs/use-pack-catalog-availability";

export default function LoginPage() {
  const router = useRouter();
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const { isEmpty: isPackCatalogEmpty, emptyStateSummary } = usePackCatalogAvailability({
    authMode: "none",
    authIdleTimeoutMs: 0,
    requestTimeoutMs: runtime.config.requestTimeoutMs,
  });

  useEffect(() => {
    if (isPackCatalogEmpty) {
      document.body.classList.add("modal-open");
      return () => document.body.classList.remove("modal-open");
    }

    document.body.classList.remove("modal-open");
  }, [isPackCatalogEmpty]);

  return (
    <>
      <LoginPanel onAuthenticated={() => router.replace("/")} />
      {isPackCatalogEmpty ? <NoValidPacksModal availabilitySummary={emptyStateSummary} /> : null}
    </>
  );
}

