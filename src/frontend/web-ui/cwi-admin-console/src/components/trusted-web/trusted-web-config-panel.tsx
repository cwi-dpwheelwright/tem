"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useAuth } from "@/lib/auth/context";
import {
  getTrustedWebConfig,
  saveTrustedWebConfig,
  type TrustedWebConfig,
  type TrustedWebSourceConfig,
} from "@/lib/trusted-web/client";

type ActionResult = {
  ok: boolean;
  status: number;
  error?: string;
};

const emptySource = (): TrustedWebSourceConfig => ({
  source_id: "",
  display_name: "",
  allowed_domains: [],
  seed_urls: [],
  sitemap_urls: [],
  rss_urls: [],
  include_path_prefixes: [],
  exclude_path_prefixes: [],
});

const emptyConfig = (): TrustedWebConfig => ({
  enabled: false,
  version: 1,
  default_profile: "general",
  profiles: {
    general: {
      mode: "trusted_sites_only",
      max_results: 5,
      sources: [emptySource()],
    },
  },
});

function toLines(values: string[] | undefined): string {
  return (values ?? []).join("\n");
}

function fromLines(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function ResultPanel({ result }: { result: ActionResult | null }) {
  if (!result) {
    return null;
  }

  const className = result.ok
    ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-100"
    : "border-amber-500/40 bg-amber-500/10 text-amber-100";

  return (
    <div className={`rounded-2xl border p-4 text-sm ${className}`}>
      <p className="font-semibold">
        {result.ok ? "Saved" : "Request failed"} (HTTP {result.status || "n/a"})
      </p>
      {result.error && <p className="mt-2 text-xs">{result.error}</p>}
    </div>
  );
}

export default function TrustedWebConfigPanel() {
  const { accessToken, identityProfile, user } = useAuth();
  const [config, setConfig] = useState<TrustedWebConfig>(() => emptyConfig());
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<ActionResult | null>(null);

  const profileName = config.default_profile || "general";
  const profile = useMemo(() => {
    return config.profiles[profileName] ?? config.profiles.general ?? emptyConfig().profiles.general;
  }, [config.profiles, profileName]);
  const actorLabel = identityProfile?.email ?? user?.profile.email ?? "superadmin";

  const updateConfig = (patch: Partial<TrustedWebConfig>) => {
    setConfig((current) => ({ ...current, ...patch }));
  };

  const updateProfile = (patch: Partial<typeof profile>) => {
    setConfig((current) => ({
      ...current,
      profiles: {
        ...current.profiles,
        [profileName]: {
          ...profile,
          ...patch,
          mode: "trusted_sites_only",
        },
      },
    }));
  };

  const updateSource = (index: number, patch: Partial<TrustedWebSourceConfig>) => {
    updateProfile({
      sources: profile.sources.map((source, sourceIndex) =>
        sourceIndex === index ? { ...source, ...patch } : source,
      ),
    });
  };

  const addSource = () => {
    updateProfile({ sources: [...profile.sources, emptySource()] });
  };

  const removeSource = (index: number) => {
    const next = profile.sources.filter((_, sourceIndex) => sourceIndex !== index);
    updateProfile({ sources: next.length > 0 ? next : [emptySource()] });
  };

  const load = useCallback(async () => {
    setLoading(true);
    setResult(null);
    const response = await getTrustedWebConfig(accessToken);
    if (response.ok && response.data) {
      const loaded = response.data;
      const loadedProfile = loaded.default_profile || "general";
      setConfig({
        ...loaded,
        default_profile: loadedProfile,
        profiles: Object.keys(loaded.profiles ?? {}).length > 0 ? loaded.profiles : emptyConfig().profiles,
      });
      setResult({ ok: true, status: response.status });
    } else {
      setResult({ ok: false, status: response.status, error: response.error });
    }
    setLoading(false);
  }, [accessToken]);

  useEffect(() => {
    if (accessToken) {
      void load();
    }
  }, [accessToken, load]);

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setLoading(true);
    setResult(null);
    const normalizedProfileName = profileName.trim() || "general";
    const payload: TrustedWebConfig = {
      ...config,
      default_profile: normalizedProfileName,
      profiles: {
        ...config.profiles,
        [normalizedProfileName]: {
          ...profile,
          mode: "trusted_sites_only",
          max_results: profile.max_results ?? 5,
          sources: profile.sources.map((source) => ({
            ...source,
            source_id: source.source_id?.trim(),
            display_name: source.display_name?.trim(),
          })),
        },
      },
    };
    const response = await saveTrustedWebConfig(payload, accessToken);
    if (response.ok && response.data) {
      setConfig(response.data);
      setResult({ ok: true, status: response.status });
    } else {
      setResult({ ok: false, status: response.status, error: response.error });
    }
    setLoading(false);
  };

  return (
    <section className="rounded-3xl border border-white/10 bg-slate-950/60 p-6 shadow-xl shadow-black/30">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold text-white">Trusted web sources</h2>
          <p className="text-sm text-slate-300">
            Configure the global trusted-site profile used by the GPT web include toggle.
          </p>
        </div>
        <span className="rounded-full border border-white/10 bg-white/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-slate-200">
          database backed
        </span>
      </header>

      <form className="mt-6 space-y-6" onSubmit={save}>
        <div className="flex flex-wrap items-end justify-between gap-4 rounded-2xl border border-white/10 bg-white/5 px-4 py-3">
          <div>
            <p className="text-xs uppercase tracking-[0.2em] text-slate-400">Scope</p>
            <p className="mt-1 text-sm font-semibold text-white">Global trusted web allowlist</p>
            <p className="mt-1 text-xs text-slate-400">Changes apply to every tenant and pack.</p>
          </div>
          <button
            className="rounded-full border border-white/20 px-5 py-2 text-sm font-semibold text-white"
            type="button"
            onClick={load}
            disabled={loading}
          >
            {loading ? "Loading..." : "Reload"}
          </button>
        </div>

        <div className="grid gap-4 md:grid-cols-3">
          <label className="flex items-center gap-3 rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-sm text-slate-200">
            <input
              type="checkbox"
              checked={config.enabled}
              onChange={(event) => updateConfig({ enabled: event.target.checked })}
            />
            Enabled
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Profile
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              value={profileName}
              onChange={(event) => {
                const nextName = event.target.value || "general";
                setConfig((current) => ({
                  ...current,
                  default_profile: nextName,
                  profiles: {
                    ...current.profiles,
                    [nextName]: current.profiles[current.default_profile] ?? profile,
                  },
                }));
              }}
              required
            />
          </label>
          <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
            Max results
            <input
              className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
              type="number"
              min={1}
              max={10}
              value={profile.max_results ?? 5}
              onChange={(event) => updateProfile({ max_results: Number(event.target.value) })}
            />
          </label>
        </div>

        <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
          Description
          <input
            className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
            value={profile.description ?? ""}
            onChange={(event) => updateProfile({ description: event.target.value })}
          />
        </label>

        <div className="space-y-4">
          {profile.sources.map((source, index) => (
            <div key={index} className="rounded-2xl border border-white/10 bg-white/5 p-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h3 className="text-sm font-semibold text-white">Source {index + 1}</h3>
                <button
                  type="button"
                  className="rounded-full border border-white/20 px-3 py-1 text-xs font-semibold text-white"
                  onClick={() => removeSource(index)}
                >
                  Remove
                </button>
              </div>

              <div className="mt-4 grid gap-4 md:grid-cols-2">
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Source ID
                  <input
                    className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={source.source_id ?? ""}
                    onChange={(event) => updateSource(index, { source_id: event.target.value })}
                  />
                </label>
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Display name
                  <input
                    className="mt-2 w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={source.display_name ?? ""}
                    onChange={(event) => updateSource(index, { display_name: event.target.value })}
                  />
                </label>
              </div>

              <div className="mt-4 grid gap-4 lg:grid-cols-3">
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Allowed domains
                  <textarea
                    className="mt-2 min-h-[110px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={toLines(source.allowed_domains)}
                    onChange={(event) => updateSource(index, { allowed_domains: fromLines(event.target.value) })}
                  />
                </label>
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Seed URLs
                  <textarea
                    className="mt-2 min-h-[110px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={toLines(source.seed_urls)}
                    onChange={(event) => updateSource(index, { seed_urls: fromLines(event.target.value) })}
                  />
                </label>
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Sitemap URLs
                  <textarea
                    className="mt-2 min-h-[110px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={toLines(source.sitemap_urls)}
                    onChange={(event) => updateSource(index, { sitemap_urls: fromLines(event.target.value) })}
                  />
                </label>
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  RSS URLs
                  <textarea
                    className="mt-2 min-h-[110px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={toLines(source.rss_urls)}
                    onChange={(event) => updateSource(index, { rss_urls: fromLines(event.target.value) })}
                  />
                </label>
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Include path prefixes
                  <textarea
                    className="mt-2 min-h-[110px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={toLines(source.include_path_prefixes)}
                    onChange={(event) => updateSource(index, { include_path_prefixes: fromLines(event.target.value) })}
                  />
                </label>
                <label className="text-xs uppercase tracking-[0.2em] text-slate-400">
                  Exclude path prefixes
                  <textarea
                    className="mt-2 min-h-[110px] w-full rounded-xl border border-white/10 bg-slate-950/50 px-3 py-2 text-sm text-white"
                    value={toLines(source.exclude_path_prefixes)}
                    onChange={(event) => updateSource(index, { exclude_path_prefixes: fromLines(event.target.value) })}
                  />
                </label>
              </div>
            </div>
          ))}
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            className="rounded-full border border-white/20 px-5 py-2 text-sm font-semibold text-white"
            onClick={addSource}
          >
            Add source
          </button>
          <button
            type="submit"
            className="rounded-full border border-sky-300/40 bg-sky-600/90 px-5 py-2 text-sm font-semibold text-white shadow-lg shadow-sky-950/30"
            disabled={loading}
          >
            {loading ? "Saving..." : "Save trusted sites"}
          </button>
          <p className="text-xs text-slate-400">
            Acting as {actorLabel}. Last update: {config.updated_utc ?? "not saved"}
          </p>
        </div>

        <ResultPanel result={result} />
      </form>
    </section>
  );
}
