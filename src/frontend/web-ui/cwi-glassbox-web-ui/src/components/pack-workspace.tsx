"use client";

import { useState } from "react";
import PackList, { type SelectedPack } from "@/components/pack-list";
import PackAdmin from "@/components/pack-admin";

export default function PackWorkspace() {
  const [selection, setSelection] = useState<SelectedPack | null>(null);

  return (
    <div className="space-y-6">
      <PackList onSelect={setSelection} />
      <PackAdmin selection={selection} />
    </div>
  );
}
