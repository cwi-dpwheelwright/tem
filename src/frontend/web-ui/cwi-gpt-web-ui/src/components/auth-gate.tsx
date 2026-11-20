"use client";

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { resolveRuntimeConfig } from "@/lib/config";
import { loadOverrides, saveAuthNotice, saveOverrides } from "@/lib/storage";
import { peekAuthToken } from "@/lib/auth";

interface AuthGateProps {
  children: ReactNode;
}

const AUTH_FREE_PATHS = new Set(["/login", "/register", "/auth/callback"]);
const OVERRIDE_NOTICE = "Authentication override removed. Login required.";
const AUTH_CHECK_INTERVAL_MS = 10000;
const AUTH_EXPIRED_EVENT = "cwi-auth-expired";

export function AuthGate({ children }: AuthGateProps) {
  const router = useRouter();
  const pathname = usePathname();
  const runtime = useMemo(() => resolveRuntimeConfig(loadOverrides()), []);
  const [overrideNotice] = useState<string | null>(() => {
    const overrides = loadOverrides();
    return overrides.authMode === "none" ? OVERRIDE_NOTICE : null;
  });
  const [checked, setChecked] = useState(false);
  const [token, setToken] = useState<string | null>(null);
  const tokenRef = useRef<string | null>(null);
  const checkedRef = useRef(false);

  useEffect(() => {
    const overrides = loadOverrides();
    if (overrides.authMode === "none") {
      // Strip the invalid override and surface a notice.
      const rest = { ...overrides };
      delete rest.authMode;
      saveOverrides(rest);
      saveAuthNotice(OVERRIDE_NOTICE);
    }
  }, []);

  useEffect(() => {
    let active = true;
    const checkAuth = async () => {
      const currentToken = await peekAuthToken(
        runtime.config.authMode,
        runtime.config.authIdleTimeoutMs,
      );
      if (!active) {
        return;
      }
      if (tokenRef.current !== currentToken) {
        tokenRef.current = currentToken;
        setToken(currentToken);
      }
      if (!checkedRef.current) {
        checkedRef.current = true;
        setChecked(true);
      }

      const onAuthFreePath = pathname ? AUTH_FREE_PATHS.has(pathname) : false;
      if (!currentToken && !onAuthFreePath) {
        router.replace("/login");
      }
    };

    const scheduleCheck = () => {
      void checkAuth();
    };

    scheduleCheck();

    const intervalId = window.setInterval(scheduleCheck, AUTH_CHECK_INTERVAL_MS);
    const onVisibility = () => {
      if (document.visibilityState === "visible") {
        scheduleCheck();
      }
    };
    const onFocus = () => {
      scheduleCheck();
    };
    const onStorage = (event: StorageEvent) => {
      if (!event.key || event.key.startsWith("cwi.user.web.token")) {
        scheduleCheck();
      }
    };
    const onAuthExpired = () => {
      scheduleCheck();
    };

    window.addEventListener("focus", onFocus);
    document.addEventListener("visibilitychange", onVisibility);
    window.addEventListener("storage", onStorage);
    window.addEventListener(AUTH_EXPIRED_EVENT, onAuthExpired);

    return () => {
      active = false;
      window.clearInterval(intervalId);
      window.removeEventListener("focus", onFocus);
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("storage", onStorage);
      window.removeEventListener(AUTH_EXPIRED_EVENT, onAuthExpired);
    };
  }, [pathname, router, runtime.config.authIdleTimeoutMs, runtime.config.authMode]);

  const onAuthFreePath = pathname ? AUTH_FREE_PATHS.has(pathname) : false;

  if (!checked) {
    return null;
  }

  if (!token && !onAuthFreePath) {
    return null;
  }

  return (
    <>
      {overrideNotice ? (
        <div className="alert alert-warning" role="status" aria-live="polite">
          {overrideNotice}
        </div>
      ) : null}
      {children}
    </>
  );
}
