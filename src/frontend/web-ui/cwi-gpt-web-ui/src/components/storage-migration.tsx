"use client";

import { useEffect } from "react";
import { runStorageMigrations } from "@/lib/storage-migrations";

export function StorageMigrationGate() {
  useEffect(() => {
    runStorageMigrations();
  }, []);

  return null;
}
