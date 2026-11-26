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

/** How the env policy treats one variable name: forward, ignore, force local, or anything else. */
export type EnvNameClass =
  | { readonly kind: "allowed"; readonly canonical: string }
  | { readonly kind: "ignored" }
  | { readonly kind: "local-only"; readonly canonical: string }
  | { readonly kind: "other" };

function startsWithAnyLower(lowerName: string, lowerPrefixes: readonly string[]): boolean {
  return lowerPrefixes.some((prefix) => lowerName.startsWith(prefix));
}

/**
 * Classifies one variable name in this order: allowlist (forward), queue infrastructure and plugin ignore lists
 * (neither forward nor block), local-only exact names, local-only prefixes, everything else. Exact names fold case
 * on win32 only; prefixes always ignore case (MSBuild property names are case-insensitive, so a Linux caller is
 * checked the same safe way).
 */
export function classifyEnvName(
  name: string,
  rules: ResolvedRemoteRules,
  platform: NodeJS.Platform = process.platform,
): EnvNameClass {
  const isWindows = platform === "win32";
  const lower = name.toLowerCase();
  const allowed = isWindows ? rules.allowedEnvLower.get(lower) : rules.allowedEnv.has(name) ? name : undefined;
  if (allowed !== undefined) {
    return { kind: "allowed", canonical: allowed };
  }
  if (
    isQueueInfraEnv(name, platform) ||
    (isWindows ? rules.ignoredEnvLower.has(lower) : rules.ignoredEnv.has(name)) ||
    startsWithAnyLower(lower, rules.ignoredEnvPrefixesLower)
  ) {
    return { kind: "ignored" };
  }
  const localOnly = isWindows ? rules.localOnlyEnvLower.get(lower) : rules.localOnlyEnv.has(name) ? name : undefined;
  if (localOnly !== undefined) {
    return { kind: "local-only", canonical: localOnly };
  }
  if (startsWithAnyLower(lower, rules.localOnlyEnvPrefixesLower)) {
    return { kind: "local-only", canonical: name };
  }
  return { kind: "other" };
}

/**
 * Checks one assignment written in the command itself (a leading `NAME=value`, `$env:NAME=...;` or `export NAME=...;`,
 * a per-step `NAME=value`, or `-e NAME=VALUE`): returns why the job must run locally, or null when the assignment may
 * go to the remote worker as written. Ignored names pass; local-only names (exact or prefix) and path values (paths
 * are not rewritten) do not.
 */
export function commandEnvLocalReason(
  name: string,
  value: string,
  rules: ResolvedRemoteRules,
  platform: NodeJS.Platform = process.platform,
): string | null {
  const cls = classifyEnvName(name, rules, platform);
  if (cls.kind === "local-only") {
    return `env var ${cls.canonical} is local-only`;
  }
  if (looksLikePathValue(value)) {
    return `env var ${cls.kind === "allowed" ? cls.canonical : name} has a path value (no path rewriting)`;
  }
  return null;
}

/**
 * Win32 spelling fix for an assignment written in the command: a name that matches an allowlisted or default
 * variable case-insensitively takes that canonical spelling, so `$env:test_seed='2'` reaches Linux as `TEST_SEED`.
 */
export function canonicalCommandEnvName(
  name: string,
  rules: ResolvedRemoteRules,
  platform: NodeJS.Platform = process.platform,
): string {
  if (platform !== "win32") {
    return name;
  }
  const lower = name.toLowerCase();
  const allowed = rules.allowedEnvLower.get(lower);
  if (allowed !== undefined) {
    return allowed;
  }
  return Object.keys(rules.defaultEnv).find((key) => key.toLowerCase() === lower) ?? name;
}

/**
 * Resolves which caller environment variables may be forwarded to a remote worker.
 * Empty/whitespace-only strings and `undefined` are treated as unset. Names are classified by `classifyEnvName`:
 * allowlisted ones forward (a path value forces local), ignored ones neither forward nor block, local-only ones
 * (exact or prefix) force local, and the rest are dropped.
 */
export function resolveCallerEnv(
  callerEnv: Readonly<NodeJS.ProcessEnv>,
  rules: ResolvedRemoteRules,
  platform: NodeJS.Platform = process.platform,
): CallerEnvResolution {
  const classified: { key: string; value: string; cls: EnvNameClass }[] = [];
  for (const [key, value] of Object.entries(callerEnv)) {
    if (value !== undefined && value.trim() !== "") {
      classified.push({ key, value, cls: classifyEnvName(key, rules, platform) });
    }
  }

  // Check local-only variables first so fail-closed precedence holds regardless of object key order.
  for (const { cls } of classified) {
    if (cls.kind === "local-only") {
      return { ok: false, variable: cls.canonical, reason: `env var ${cls.canonical} is local-only` };
    }
  }

  const forwarded: Record<string, string> = Object.create(null) as Record<string, string>;
  for (const { key, value, cls } of classified) {
    if (cls.kind !== "allowed") {
      continue;
    }
    if (looksLikePathValue(value)) {
      return {
        ok: false,
        variable: cls.canonical,
        reason: `env var ${cls.canonical} has a path value (no path rewriting)`,
      };
    }
    if (key === cls.canonical || forwarded[cls.canonical] === undefined) {
      forwarded[cls.canonical] = value;
    }
  }

  return { ok: true, forwarded };
}
