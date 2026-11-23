// Fast content-addressed worktree manifest and delta computation for remote CPU offload.
// Uses `git ls-files -co --exclude-standard -z` + a local (size, mtimeMs) -> sha256 stat cache so computing the full
// worktree manifest takes ~75 ms instead of re-hashing every unchanged file.

import { execFileSync } from "node:child_process";
import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as path from "node:path";
import { replaceFile } from "./history.ts";

export interface StatCacheEntry {
  size: number;
  mtimeMs: number;
  sha256: string;
}

export interface RemoteManifestCache {
  version: 1;
  digest: string;
  files: Record<string, string>;
}

export interface ManifestDiff {
  toUpload: string[];
  toDelete: string[];
  localDigest: string;
}

const ExcludedPrefixes = [
  "tmp/",
  "TestResults/",
  "BenchmarkDotNet.Artifacts/",
  "scratch/",
  ".git/",
  ".vs/",
  ".claude/worktrees/",
  "node_modules/",
  "src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded/",
];
const ExcludedSegment = /(?:^|\/)(?:bin|obj|node_modules)\//;
const ExcludedFile = /(?:\.received\.[^/]+|\.(?:log|user))$/i;

/** True when a worktree-relative POSIX path should be excluded from remote source sync. */
export function isExcludedSyncPath(relPosix: string): boolean {
  if (relPosix === "" || relPosix.startsWith("..") || relPosix.startsWith("/")) {
    return true;
  }
  for (const prefix of ExcludedPrefixes) {
    if (relPosix.startsWith(prefix)) {
      return true;
    }
  }
  return ExcludedSegment.test(relPosix) || ExcludedFile.test(relPosix);
}

/** Deterministic `sha256sum -c` manifest text (`<hex>  ./<rel>\n`) sorted by path. */
export function formatSha256Manifest(files: Readonly<Record<string, string>>): string {
  const keys = Object.keys(files).sort();
  let out = "";
  for (const key of keys) {
    out += `${files[key] ?? ""}  ./${key}\n`;
  }
  return out;
}

/** Set of all POSIX parent directories present in `files` (used to validate remote `*.received.*` targets). */
export function manifestParentDirs(files: Readonly<Record<string, string>>): Set<string> {
  const dirs = new Set<string>();
  for (const rel of Object.keys(files)) {
    const dir = path.posix.dirname(rel);
    if (dir !== "" && dir !== ".") {
      dirs.add(dir);
    }
  }
  return dirs;
}

/** Deterministic SHA-256 digest of an entire file->sha256 manifest. */
export function manifestDigest(files: Readonly<Record<string, string>>): string {
  const hash = crypto.createHash("sha256");
  const keys = Object.keys(files).sort();
  for (const key of keys) {
    hash.update(key);
    hash.update("\0");
    hash.update(files[key] ?? "");
    hash.update("\n");
  }
  return hash.digest("hex");
}

/** Computes which files must be uploaded or deleted to turn `remote` into `local`. */
export function diffManifests(
  local: Readonly<Record<string, string>>,
  remote: Readonly<Record<string, string>>,
): ManifestDiff {
  const toUpload: string[] = [];
  const toDelete: string[] = [];
  for (const [rel, sha] of Object.entries(local)) {
    if (remote[rel] !== sha) {
      toUpload.push(rel);
    }
  }
  for (const rel of Object.keys(remote)) {
    if (!(rel in local)) {
      toDelete.push(rel);
    }
  }
  toUpload.sort();
  toDelete.sort();
  return { toUpload, toDelete, localDigest: manifestDigest(local) };
}

/** Parses `sha256sum` output (`<hex>  ./<rel>` or `<hex>  <rel>`) into a relPosix -> sha256 map. */
export function parseRemoteSha256Output(stdout: string): Record<string, string> {
  const files = Object.create(null) as Record<string, string>;
  for (const rawLine of stdout.split("\n")) {
    const line = rawLine.replace(/\r$/, "");
    if (line === "") {
      continue;
    }
    const escaped = line.startsWith("\\");
    const body = escaped ? line.slice(1) : line;
    const match = /^([0-9a-f]{64}) [ *](.+)$/.exec(body);
    if (match === null) {
      continue;
    }
    const sha = match[1] as string;
    let rel = (match[2] as string).replace(/^\.\//, "");
    if (escaped) {
      rel = rel.replace(/\\([\\n])/g, (_, ch: string) => (ch === "n" ? "\n" : "\\"));
    }
    if (!isExcludedSyncPath(rel)) {
      files[rel] = sha;
    }
  }
  return files;
}

function loadJson<T>(filePath: string): T | null {
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8")) as T;
  } catch {
    return null;
  }
}

function saveJsonAtomic(filePath: string, value: unknown): void {
  const temp = `${filePath}.${process.pid}.tmp`;
  try {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(temp, JSON.stringify(value));
    replaceFile(temp, filePath);
  } catch {
    fs.rmSync(temp, { force: true });
  }
}

export function loadRemoteManifestCache(cachePath: string): RemoteManifestCache | null {
  const parsed = loadJson<RemoteManifestCache>(cachePath);
  if (
    parsed !== null &&
    parsed.version === 1 &&
    typeof parsed.digest === "string" &&
    parsed.files !== null &&
    typeof parsed.files === "object"
  ) {
    return parsed;
  }
  return null;
}

export function saveRemoteManifestCache(cachePath: string, files: Record<string, string>): RemoteManifestCache {
  const cache: RemoteManifestCache = { version: 1, digest: manifestDigest(files), files };
  saveJsonAtomic(cachePath, cache);
  return cache;
}

/**
 * Collects the SHA-256 manifest of all tracked + untracked (non-ignored) files in `worktree`, using an on-disk
 * `(size, mtimeMs) -> sha256` stat cache so unchanged files are not re-read.
 */
export function collectLocalManifest(worktree: string, statCachePath: string): Record<string, string> {
  const stdout = execFileSync("git", ["ls-files", "-co", "--exclude-standard", "-z"], {
    cwd: worktree,
    maxBuffer: 64 * 1024 * 1024,
    windowsHide: true,
  }).toString("utf8");

  const rawCache = loadJson<Record<string, StatCacheEntry>>(statCachePath) ?? {};
  const nextCache = Object.create(null) as Record<string, StatCacheEntry>;
  const files = Object.create(null) as Record<string, string>;
  let dirty = false;

  for (const entry of stdout.split("\0")) {
    if (entry === "") {
      continue;
    }
    const rel = entry.split("\\").join("/");
    if (isExcludedSyncPath(rel)) {
      continue;
    }
    const abs = path.join(worktree, rel);
    let stat: fs.Stats;
    try {
      stat = fs.lstatSync(abs);
    } catch {
      continue; // deleted in working tree
    }
    if (!stat.isFile()) {
      continue;
    }
    const cacheKey = `${worktree}\0${rel}`;
    const cached = rawCache[cacheKey];
    if (cached !== undefined && cached.size === stat.size && cached.mtimeMs === stat.mtimeMs) {
      files[rel] = cached.sha256;
      nextCache[cacheKey] = cached;
    } else {
      const sha256 = crypto.createHash("sha256").update(fs.readFileSync(abs)).digest("hex");
      files[rel] = sha256;
      nextCache[cacheKey] = { size: stat.size, mtimeMs: stat.mtimeMs, sha256 };
      dirty = true;
    }
  }

  // Keep entries of other worktrees in the shared stat cache so multiple worktrees do not evict each other
  const prefix = `${worktree}\0`;
  for (const [k, v] of Object.entries(rawCache)) {
    if (!k.startsWith(prefix)) {
      nextCache[k] = v;
    }
  }
  if (dirty) {
    saveJsonAtomic(statCachePath, nextCache);
  }
  return files;
}
