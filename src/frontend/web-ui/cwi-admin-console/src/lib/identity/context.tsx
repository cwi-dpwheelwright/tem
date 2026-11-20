"use client";

import { createContext, useContext, useMemo, useState } from "react";

interface IdentityTenantContextValue {
  tenantId: string;
  setTenantId: (value: string) => void;
  clearTenant: () => void;
}

const IdentityTenantContext = createContext<IdentityTenantContextValue | null>(null);

export function IdentityTenantProvider({ children }: { children: React.ReactNode }) {
  const [tenantId, setTenantId] = useState("");

  const value = useMemo(
    () => ({
      tenantId,
      setTenantId: (value: string) => setTenantId(value.trim()),
      clearTenant: () => setTenantId(""),
    }),
    [tenantId],
  );

  return <IdentityTenantContext.Provider value={value}>{children}</IdentityTenantContext.Provider>;
}

export function useIdentityTenant(): IdentityTenantContextValue {
  const ctx = useContext(IdentityTenantContext);
  if (!ctx) {
    throw new Error("useIdentityTenant must be used within IdentityTenantProvider.");
  }
  return ctx;
}
