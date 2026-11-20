import type { PackSummary } from "./catalog";
import type { VerifiedPack } from "./types";

function normalizeSelection(value: string | null): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function matchesSelection(candidate: string | undefined, selection: string): boolean {
  if (!candidate) {
    return false;
  }

  const normalizedCandidate = candidate.trim();
  return (
    normalizedCandidate === selection ||
    normalizedCandidate.toLowerCase() === selection.toLowerCase()
  );
}

export function findMatchingPackSummary(
  selectedPackName: string | null,
  cached: VerifiedPack | null,
  packs: PackSummary[],
): PackSummary | null {
  if (!selectedPackName) {
    return null;
  }

  const normalizedSelection = selectedPackName.trim();
  const normalizedHash = cached?.hash.trim().toLowerCase() ?? null;

  return packs.find((item) => {
    const packId = item.pack_id?.trim();
    const packName = item.name?.trim();
    if (packName && packName === normalizedSelection) {
      return true;
    }
    if (packId && packId === normalizedSelection) {
      return true;
    }
    return Boolean(normalizedHash && item.latest && item.latest.toLowerCase() === normalizedHash);
  }) ?? null;
}

export function shouldReconcileLegacyPackSelection(
  selectedPackName: string | null,
  cached: VerifiedPack | null,
): boolean {
  if (!selectedPackName || !cached) {
    return false;
  }

  return /\s/.test(selectedPackName) || cached.displayName === selectedPackName;
}

export function resolveCanonicalPackSelection(
  selectedPackName: string | null,
  cached: VerifiedPack | null,
  packs: PackSummary[],
): string | null {
  if (!selectedPackName) {
    return null;
  }

  const normalizedSelection = selectedPackName.trim();
  const match = findMatchingPackSummary(selectedPackName, cached, packs);
  const resolvedId = match?.pack_id?.trim() || match?.name?.trim();
  if (!resolvedId || resolvedId === normalizedSelection) {
    return null;
  }

  return resolvedId;
}

export function isPackSelectionAvailable(
  selectedPackName: string | null,
  cached: VerifiedPack | null,
  packs: PackSummary[],
): boolean {
  return Boolean(findMatchingPackSummary(selectedPackName, cached, packs));
}

export function buildReconciledVerifiedPack(cached: VerifiedPack, resolvedId: string): VerifiedPack {
  return {
    ...cached,
    name: resolvedId,
    displayName: cached.displayName ?? cached.name,
  };
}

export function findMatchingVerifiedPack(
  selectedPackName: string | null,
  packs: VerifiedPack[],
): VerifiedPack | null {
  const normalizedSelection = normalizeSelection(selectedPackName);
  if (!normalizedSelection) {
    return null;
  }

  const exact = packs.find((pack) =>
    matchesSelection(pack.name, normalizedSelection) ||
    matchesSelection(pack.displayName, normalizedSelection),
  );

  return exact ?? null;
}

export function resolveCanonicalVerifiedPackSelection(
  selectedPackName: string | null,
  packs: VerifiedPack[],
): string | null {
  const normalizedSelection = normalizeSelection(selectedPackName);
  if (!normalizedSelection) {
    return null;
  }

  const match = findMatchingVerifiedPack(normalizedSelection, packs);
  if (!match || matchesSelection(match.name, normalizedSelection)) {
    return null;
  }

  return match.name;
}

export function isVerifiedPackSelectionAvailable(
  selectedPackName: string | null,
  packs: VerifiedPack[],
): boolean {
  return Boolean(findMatchingVerifiedPack(selectedPackName, packs));
}
