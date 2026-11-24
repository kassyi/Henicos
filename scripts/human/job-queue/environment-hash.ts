// The environment of a queued job as the scheduler logs it: a short hash of each variable's value by name, and one
// hash of them all, so that two runs of the same command can be told apart by which variables differed, never by
// their values (which never leave the client).

import { createHash } from "node:crypto";
import { CommandVariable, CpusVariable, JobVariable, MsbuildArgsVariable, TurnVariable } from "./constants.ts";

/**
 * Variables whose values differ from run to run by nature, left out of the hash: the terminal's session and the
 * shell's own bookkeeping, and the variables the queue sets for a job's command. A plugin adds its own (volatileEnv:
 * the chat session ids of the agents plugin).
 */
export const VolatileEnv: readonly string[] = [
  "WT_SESSION",
  "TERM_SESSION_ID",
  "OLDPWD",
  "PWD",
  "SHLVL",
  "_",
  JobVariable,
  CpusVariable,
  TurnVariable,
  CommandVariable,
  MsbuildArgsVariable,
];

/** At most this many variables are sent; an environment larger than that is cut, in name order. */
export const MaxEnvKeys = 1000;

export interface EnvironmentHash {
  /** One hash of every name and value hash, 12 hex digits. */
  hash: string;
  /** Per variable name, a hash of its value, 8 hex digits. */
  keys: Record<string, string>;
}

const sha256 = (text: string): string => createHash("sha256").update(text).digest("hex");

/**
 * The hash of `env` without the names in `leaveOut` (VolatileEnv and the plugins' names). On Windows, where the names
 * of variables have no case (one process sees Path, another PATH), the names are kept in upper case.
 */
export function environmentHash(env: Readonly<NodeJS.ProcessEnv>, leaveOut: Iterable<string>): EnvironmentHash {
  const fold = (name: string): string => (process.platform === "win32" ? name.toUpperCase() : name);
  const left = new Set([...leaveOut].map(fold));
  const names = Object.keys(env)
    .filter((name) => env[name] !== undefined && !left.has(fold(name)))
    .sort((a, b) => (fold(a) < fold(b) ? -1 : fold(a) > fold(b) ? 1 : 0))
    .slice(0, MaxEnvKeys);
  // Without a prototype, so that a variable named "__proto__" stays a plain key
  const keys = Object.create(null) as Record<string, string>;
  for (const name of names) {
    keys[fold(name)] = sha256(String(env[name])).slice(0, 8);
  }
  return {
    hash: sha256(
      Object.keys(keys)
        .map((name) => `${name}=${keys[name]}`)
        .join("\n"),
    ).slice(0, 12),
    keys,
  };
}

/** The names whose value hashes differ between two environments, a name in only one of them included, sorted. */
export function differingNames(a: Readonly<Record<string, string>>, b: Readonly<Record<string, string>>): string[] {
  const names = new Set([...Object.keys(a), ...Object.keys(b)]);
  return [...names].filter((name) => a[name] !== b[name]).sort();
}
