"use client";

import { Modal } from "@/components/modal";
import type { PackAvailabilitySummary } from "@/lib/packs/client";

interface NoValidPacksModalProps {
  availabilitySummary?: PackAvailabilitySummary | null;
}

function formatPackCount(count: number, noun = "pack") {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

export function NoValidPacksModal({ availabilitySummary = null }: NoValidPacksModalProps) {
  const hasExactReasons = !!availabilitySummary && availabilitySummary.reasons.length > 0;
  const availablePackCount = availabilitySummary?.available_pack_count ?? 0;
  const filteredPackCount = availabilitySummary?.filtered_pack_count ?? 0;
  const unindexedPackCount = availabilitySummary?.unindexed_pack_count ?? 0;
  const indexedPackCount = availabilitySummary?.indexed_pack_count ?? 0;
  const discoveredPackCount = availabilitySummary?.discovered_pack_count ?? 0;

  return (
    <Modal ariaLabel="No valid packs available" onClose={() => {}} dismissible={false}>
      <div className="card-light-compact home-upload-feedback">
        <div>
          <h2 className="card-title">No valid packs available</h2>
          <p className="card-subtitle">
            {hasExactReasons
              ? "Pack service validation reported these exact availability problems:"
              : "The site did not return any dependency-verified packs right now. This usually means one of the following:"}
          </p>
          {availabilitySummary ? (
            <p className="card-text-muted-xs-tight">
              Pack service found {formatPackCount(indexedPackCount, "indexed pack")}, {formatPackCount(filteredPackCount, "filtered pack")}, {formatPackCount(unindexedPackCount, "unindexed pack")}, and {formatPackCount(discoveredPackCount, "pack")} on disk.
            </p>
          ) : null}
          {hasExactReasons ? (
            <ul className="card-text-muted-xs-tight">
              {availabilitySummary.reasons.map((reason) => (
                <li key={reason.code}>{reason.message}</li>
              ))}
            </ul>
          ) : (
            <ul className="card-text-muted-xs-tight">
              <li>No packs are published for this site.</li>
              <li>Published packs are missing required dependency packs.</li>
              <li>Published pack metadata or schema is invalid.</li>
              <li>Packs exist on disk but are not indexed for serving.</li>
            </ul>
          )}
          <p className="card-subtitle">
            Please contact the site administrator before using CWI GPT.
          </p>
        </div>

        <div className="alert alert-warning" role="alert">
          {availabilitySummary
            ? `Pack service currently reports ${formatPackCount(availablePackCount)} available, ${formatPackCount(filteredPackCount, "filtered pack")}, and ${formatPackCount(unindexedPackCount, "unindexed pack")}. CWI GPT cannot be used until at least one dependency-verified pack passes validation and is indexed for serving.`
            : "CWI GPT cannot be used until an administrator publishes or repairs at least one dependency-verified pack and resolves any indexing, schema, or dependency errors."}
        </div>
      </div>
    </Modal>
  );
}
