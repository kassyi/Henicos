// Layer 3 Caller Environment Policy:
// Filters caller environment variables for remote CPU offload: forwards non-path allowlisted variables in canonical
// casing, ignores queue infrastructure and standard OS variables, and fails closed on local-only or path-valued vars.

import type { ResolvedRemoteRules } from "./rules.ts";

export type CallerEnvResolution =
  | { readonly ok: true; readonly forwarded: Record<string, string> }
  | { readonly ok: false; readonly variable: string; readonly reason: string };

const SafeEnvKeyPattern = /^[A-Za-z_][A-Za-z0-9_]*$/;
const ForbiddenEnvKeys = new Set(["__proto__", "constructor", "prototype"]);

/** True when `key` is a valid environment variable identifier and not an Object.prototype trap key. */
export function isSafeEnvKey(key: string): boolean {
  return SafeEnvKeyPattern.test(key) && !ForbiddenEnvKeys.has(key);
}

/** True for internal job-queue / gpu-lock / pool-watch variables that must never trigger fallback or forward. */
export function isQueueInfraEnv(name: string, platform: NodeJS.Platform = process.platform): boolean {
  const key = platform === "win32" ? name.toUpperCase() : name;
  return (
    key.startsWith("GSHARP_JOB_QUEUE_") ||
    key.startsWith("JOB_QUEUE_") ||
    key.startsWith("GSHARP_GPU_LOCK_") ||
    key.startsWith("GSHARP_GPU_POOL_WATCH") ||
    key === "GSHARP_GPU_HOLD_WATCH"
  );
}

/**
 * True when an allowlisted variable's value looks like a filesystem path or contains control characters.
 * Preserves non-path scalars such as `"address,undefined"`, `"1"`, `"true"`, `"gcdump"`, `"42"`, `"en-US"`, `"C.UTF-8"`.
 */
export function looksLikePathValue(value: string): boolean {
  const trimmed = value.trim();
  return (
    value.includes("/") ||
    value.includes("\\") ||
    /^[A-Za-z]:/.test(trimmed) ||
    trimmed === "." ||
    trimmed === ".." ||
    trimmed === "~" ||
    /[\u0000-\u001f\u007f-\u009f]/.test(value)
  );
}

/** Platform-aware existence check so caller env cannot shadow differently-cased leading assignments on Windows. */
export function hasEnvKey(
  env: Readonly<Record<string, string>>,
  key: string,
  platform: NodeJS.Platform = process.platform,
): boolean {
  if (platform !== "win32") {
    return Object.hasOwn(env, key);
  }
  const lower = key.toLowerCase();
  return Object.keys(env).some((existing) => existing.toLowerCase() === lower);
}

/** Platform-aware lookup for internal queue control flags in `options.env`. */
export function getEnvValue(
  env: Readonly<NodeJS.ProcessEnv>,
  key: string,
  platform: NodeJS.Platform = process.platform,
): string | undefined {
  if (platform !== "win32") {
    return Object.hasOwn(env, key) ? env[key] : undefined;
  }
  if (Object.hasOwn(env, key)) {
    const exact = env[key];
    if (exact !== undefined) {
      return exact;
    }
  }
  const lower = key.toLowerCase();
  for (const [k, v] of Object.entries(env)) {
    if (k.toLowerCase() === lower) {
      return v;
    }
  }
  return undefined;
}

/**
 * Resolves which caller environment variables may be forwarded to a remote worker.
 * Empty/whitespace-only strings and `undefined` are treated as unset; `localOnlyEnv` takes precedence over `allowedEnv`.
 */
export function resolveCallerEnv(
  callerEnv: Readonly<NodeJS.ProcessEnv>,
  rules: ResolvedRemoteRules,
  platform: NodeJS.Platform = process.platform,
): CallerEnvResolution {
  const isWindows = platform === "win32";
  const entries: readonly [string, string][] = Object.entries(callerEnv).filter(
    (entry): entry is [string, string] =>
      entry[1] !== undefined && entry[1].trim() !== "" && !isQueueInfraEnv(entry[0], platform),
  );

  // Check local-only variables first so fail-closed precedence holds regardless of object key order.
  for (const [rawKey] of entries) {
    const localCanonical = isWindows
      ? rules.localOnlyEnvLower.get(rawKey.toLowerCase())
      : rules.localOnlyEnv.has(rawKey)
        ? rawKey
        : undefined;
    if (localCanonical !== undefined) {
      return {
        ok: false,
        variable: localCanonical,
        reason: `env var ${localCanonical} is local-only`,
      };
    }
  }

  const forwarded: Record<string, string> = Object.create(null) as Record<string, string>;
  for (const [rawKey, rawValue] of entries) {
    const allowedCanonical = isWindows
      ? rules.allowedEnvLower.get(rawKey.toLowerCase())
      : rules.allowedEnv.has(rawKey)
        ? rawKey
        : undefined;
    if (allowedCanonical === undefined) {
      continue;
    }
    if (looksLikePathValue(rawValue)) {
      return {
        ok: false,
        variable: allowedCanonical,
        reason: `env var ${allowedCanonical} has a path value (no path rewriting)`,
      };
    }
    if (rawKey === allowedCanonical || forwarded[allowedCanonical] === undefined) {
      forwarded[allowedCanonical] = rawValue;
    }
  }

  return { ok: true, forwarded };
}
