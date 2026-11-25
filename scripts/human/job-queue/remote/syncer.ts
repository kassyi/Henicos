// Layer 3 Workspace Synchronization:
//   - Pure manifest helpers (local stat-cached SHA-256 collection, diffing, single-source exclusion/prune rules)
//   - `WorkspaceSyncer`: cohesive collaborator depending only on `RemoteTransport` (Port) to keep remote sources
//     cryptographically aligned with the local worktree while preserving warm build outputs.

import { execFileSync } from "node:child_process";
import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as path from "node:path";
import { replaceFile } from "../history.ts";
import { prefix } from "../system.ts";
import { sanitizeRemoteLogText } from "./artifact-collector.ts";
import {
  bashQuote,
  bashWorkspaceExpr,
  RemoteLockFile,
  RemoteManifestDigestFile,
  RemoteManifestSha256File,
  RemoteMetadataFiles,
  RemoteWorkspaceMarkerFile,
} from "./spec.ts";
import type { RemoteTransport } from "./transport.ts";

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

/** Optional plugin-supplied sync exclusions merged with the core defaults. */
export interface RemoteSyncRules {
  readonly excludePrefixes?: readonly string[];
  readonly excludeFindNames?: readonly string[];
}

export interface SyncOutcome {
  ok: true;
  localManifest: Record<string, string>;
  sha256ManifestText: string;
  digest: string;
  uploaded: number;
  deleted: number;
  elapsedSec: string;
}

export interface SyncFailure {
  ok: false;
  error: string;
}

export const BaseExcludedPrefixes: readonly string[] = [
  "tmp/",
  "scratch/",
  ".git/",
  ".vs/",
  ".claude/worktrees/",
  "node_modules/",
];
const ExcludedSegment = /(?:^|\/)(?:bin|obj|node_modules)\//;
export const BaseExcludedFindNames: readonly string[] = ["*.log"];

/** Converts a simple `find -name` glob (with `*` and `?`) into a basename regex. */
export function globNameToRegExp(glob: string): RegExp {
  const escaped = glob
    .replace(/[.+^${}()|[\]\\]/g, "\\$&")
    .replace(/\*/g, ".*")
    .replace(/\?/g, ".");
  return new RegExp(`^${escaped}$`, "i");
}

function allExcludedPrefixes(rules?: RemoteSyncRules): string[] {
  const out = [...BaseExcludedPrefixes];
  if (rules?.excludePrefixes !== undefined) {
    for (const p of rules.excludePrefixes) {
      const normalized = path.posix.normalize(p).replace(/\/+$/, "");
      if (
        normalized !== "" &&
        normalized !== "." &&
        normalized !== ".." &&
        !normalized.startsWith("../") &&
        !normalized.startsWith("/")
      ) {
        const withSlash = `${normalized}/`;
        if (!out.includes(withSlash)) {
          out.push(withSlash);
        }
      }
    }
  }
  return out;
}

function allExcludedFindNames(rules?: RemoteSyncRules): string[] {
  const out = [...BaseExcludedFindNames];
  if (rules?.excludeFindNames !== undefined) {
    for (const glob of rules.excludeFindNames) {
      if (glob !== "" && !out.includes(glob)) {
        out.push(glob);
      }
    }
  }
  return out;
}

function singleQuote(value: string): string {
  return `'${value.replace(/'/g, "'\\''")}'`;
}

/**
 * Builds the remote `find . <expr>` prune/print0 expression matching `isExcludedSyncPath` so local and remote
 * source-file sets stay identical while keeping build outputs warm.
 */
export function buildRemoteFindPruneExpr(rules?: RemoteSyncRules, extraRootFiles: readonly string[] = []): string {
  const dirPruneTerms: string[] = [];
  for (const p of allExcludedPrefixes(rules)) {
    dirPruneTerms.push(`-path ${singleQuote(`./${p.replace(/\/+$/, "")}`)}`);
  }
  dirPruneTerms.push("-name 'bin'", "-name 'obj'", "-name 'node_modules'");
  const pruneTerms: string[] = [
    ...extraRootFiles.map((file) => `-path ${singleQuote(`./${file}`)}`),
    `-type d \\( ${dirPruneTerms.join(" -o ")} \\)`,
  ];
  const nameNegations = allExcludedFindNames(rules)
    .map((glob) => `! -iname ${singleQuote(glob)}`)
    .join(" ");
  const fileFilter = nameNegations !== "" ? `-type f ${nameNegations} -print0` : "-type f -print0";
  return `\\( ${pruneTerms.join(" -o ")} \\) -prune -o \\( ${fileFilter} \\)`;
}

/** True when a worktree-relative POSIX path should be excluded from remote source sync. */
export function isExcludedSyncPath(relPosix: string, rules?: RemoteSyncRules): boolean {
  if (relPosix === "" || relPosix === ".." || relPosix.startsWith("../") || relPosix.startsWith("/")) {
    return true;
  }
  for (const p of allExcludedPrefixes(rules)) {
    if (relPosix.startsWith(p)) {
      return true;
    }
  }
  if (ExcludedSegment.test(relPosix)) {
    return true;
  }
  const base = path.posix.basename(relPosix);
  for (const glob of allExcludedFindNames(rules)) {
    if (globNameToRegExp(glob).test(base)) {
      return true;
    }
  }
  return false;
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

/** Set of all POSIX parent directories present in `files` (used to validate failure artifact targets). */
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
export function parseRemoteSha256Output(stdout: string, _rules?: RemoteSyncRules): Record<string, string> {
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
    if (rel !== "" && rel !== ".." && !rel.startsWith("../") && !rel.startsWith("/")) {
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
  const temp = `${filePath}.${process.pid}.${crypto.randomUUID()}.tmp`;
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
export function collectLocalManifest(
  worktree: string,
  statCachePath: string,
  rules?: RemoteSyncRules,
): Record<string, string> {
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
    if (isExcludedSyncPath(rel, rules)) {
      continue;
    }
    const abs = path.join(worktree, rel);
    let stat: fs.Stats;
    try {
      stat = fs.lstatSync(abs);
    } catch {
      continue;
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

  const keyPrefix = `${worktree}\0`;
  for (const [k, v] of Object.entries(rawCache)) {
    if (!k.startsWith(keyPrefix)) {
      nextCache[k] = v;
    }
  }
  if (dirty) {
    saveJsonAtomic(statCachePath, nextCache);
  }
  return files;
}

function buildWorkspacePrepLines(): string[] {
  return [
    `if [ ! -f "$WS/${RemoteWorkspaceMarkerFile}" ]; then`,
    '  if [ ! -d "$WS" ] || [ -z "$(ls -A "$WS" 2>/dev/null)" ]; then',
    '    mkdir -p -- "$WS"',
    `    : > "$WS/${RemoteWorkspaceMarkerFile}"`,
    "  else",
    `    echo "refusing to use non-empty remote workspace without ${RemoteWorkspaceMarkerFile}; re-run provision-remote-worker.js --apply or create the marker file" >&2`,
    "    exit 1",
    "  fi",
    "fi",
  ];
}

/** Cohesive workspace synchronizer operating strictly over `RemoteTransport` (Port). */
export class WorkspaceSyncer {
  private readonly transport: RemoteTransport;
  private readonly syncRules: RemoteSyncRules;
  private readonly stateDir: string;
  readonly pruneExpr: string;

  constructor(transport: RemoteTransport, syncRules: RemoteSyncRules, stateDir: string) {
    this.transport = transport;
    this.syncRules = syncRules;
    this.stateDir = stateDir;
    this.pruneExpr = buildRemoteFindPruneExpr(syncRules, RemoteMetadataFiles);
  }

  private get remoteCachePath(): string {
    return path.join(this.stateDir, `remote-manifest-${this.transport.spec.name}.json`);
  }

  private get statCachePath(): string {
    return path.join(this.stateDir, "offload-stat-cache.json");
  }

  /** Fetches the remote workspace's current file->sha256 map. */
  async fetchRemoteManifest(signal?: AbortSignal): Promise<Record<string, string> | null> {
    if (signal?.aborted) {
      return null;
    }
    const ws = bashWorkspaceExpr(this.transport.spec.workspace);
    const script = [
      "set -euo pipefail",
      `WS=${ws}`,
      ...buildWorkspacePrepLines(),
      `cd "$WS"`,
      `find . ${this.pruneExpr} | xargs -0 -r sha256sum`,
    ].join("\n");
    const res = await this.transport.exec(script, signal);
    return res.code === 0 ? parseRemoteSha256Output(res.stdout, this.syncRules) : null;
  }

  /** Applies a delta upload/delete + `.job-queue-manifest.sha256` atomically under the remote flock. */
  private async applyDelta(
    worktree: string,
    diff: ManifestDiff,
    sha256ManifestText: string,
    expectedRemoteDigest?: string,
    signal?: AbortSignal,
  ): Promise<{ ok: boolean; stale?: boolean; error?: string }> {
    if (signal?.aborted) {
      return { ok: false, error: "aborted" };
    }
    const ws = bashWorkspaceExpr(this.transport.spec.workspace);
    const deleteBuf =
      diff.toDelete.length === 0 ? Buffer.alloc(0) : Buffer.from(`${diff.toDelete.join("\0")}\0`, "utf8");
    const manifestBuf = Buffer.from(sha256ManifestText, "utf8");
    const quotedDigest = expectedRemoteDigest !== undefined ? bashQuote(expectedRemoteDigest) : "";
    const guard =
      expectedRemoteDigest !== undefined
        ? [
            `if [ "$(cat -- ${RemoteManifestDigestFile} 2>/dev/null || true)" != ${quotedDigest} ]; then`,
            "  exit 254",
            "fi",
          ].join("\n")
        : "";
    const delCmd = 'dd iflag=fullblock bs="$DEL_BYTES" count=1 2>/dev/null | xargs -0 -r rm -f --';
    const manCmd = `dd iflag=fullblock bs="$MANIFEST_BYTES" count=1 2>/dev/null > ${RemoteManifestSha256File}.tmp`;
    const remoteCmd = [
      "bash -c",
      bashQuote(
        [
          "set -euo pipefail",
          `WS=${ws}`,
          ...buildWorkspacePrepLines(),
          `exec 9>"$WS/${RemoteLockFile}"`,
          "flock 9",
          `cd "$WS"`,
          `if [ ! -f ${RemoteWorkspaceMarkerFile} ]; then exit 1; fi`,
          guard,
          "IFS= read -r DEL_BYTES",
          `if [ "$DEL_BYTES" -gt 0 ]; then ${delCmd}; fi`,
          "IFS= read -r MANIFEST_BYTES",
          `if [ "$MANIFEST_BYTES" -gt 0 ]; then ${manCmd}; else : > ${RemoteManifestSha256File}.tmp; fi`,
          diff.toUpload.length > 0 ? "tar -xmf -" : "",
          `mv -f ${RemoteManifestSha256File}.tmp ${RemoteManifestSha256File}`,
          `printf '%s\\n' ${bashQuote(diff.localDigest)} > ${RemoteManifestDigestFile}`,
        ]
          .filter(Boolean)
          .join("\n"),
      ),
    ].join(" ");

    const headerChunks: Buffer[] = [Buffer.from(`${deleteBuf.length}\n`, "utf8")];
    if (deleteBuf.length > 0) {
      headerChunks.push(deleteBuf);
    }
    headerChunks.push(Buffer.from(`${manifestBuf.length}\n`, "utf8"), manifestBuf);

    const res = await this.transport.streamUpload(remoteCmd, headerChunks, worktree, diff.toUpload, signal);
    if (res.code === 254) {
      return { ok: false, stale: true };
    }
    return res.code === 0 ? { ok: true } : { ok: false, error: res.stderr.trim() || `ssh exit ${res.code}` };
  }

  /** Performs a full fetch-and-heal sync against the remote workspace (used on cold cache or exit 254). */
  async resyncClean(
    worktree: string,
    localManifest: Record<string, string>,
    sha256ManifestText: string,
    signal?: AbortSignal,
  ): Promise<{ uploaded: number; deleted: number; digest: string } | null> {
    if (signal?.aborted) {
      return null;
    }
    const fetched = await this.fetchRemoteManifest(signal);
    if (fetched === null || signal?.aborted) {
      return null;
    }
    const diff = diffManifests(localManifest, fetched);
    const applied = await this.applyDelta(worktree, diff, sha256ManifestText, undefined, signal);
    if (!applied.ok) {
      const target = `${this.transport.spec.name} (${this.transport.spec.host})`;
      prefix(`remote sync to ${target} failed: ${sanitizeRemoteLogText(applied.error ?? "unknown")}`);
      return null;
    }
    saveRemoteManifestCache(this.remoteCachePath, localManifest);
    return { uploaded: diff.toUpload.length, deleted: diff.toDelete.length, digest: diff.localDigest };
  }

  /**
   * Computes the local manifest and syncs changes to the remote workspace (falling back to full resync on stale cache).
   */
  async sync(
    worktree: string,
    localManifestOverride?: Record<string, string>,
    signal?: AbortSignal,
  ): Promise<SyncOutcome | SyncFailure> {
    if (signal?.aborted) {
      return { ok: false, error: "aborted" };
    }
    const syncStart = performance.now();
    const localManifest = localManifestOverride ?? collectLocalManifest(worktree, this.statCachePath, this.syncRules);
    const sha256ManifestText = formatSha256Manifest(localManifest);
    const cached = loadRemoteManifestCache(this.remoteCachePath);

    let uploaded = 0;
    let deleted = 0;
    let digest = "";

    if (cached === null) {
      const res = await this.resyncClean(worktree, localManifest, sha256ManifestText, signal);
      if (res === null) {
        return { ok: false, error: "failed to fetch/sync remote manifest" };
      }
      uploaded = res.uploaded;
      deleted = res.deleted;
      digest = res.digest;
    } else {
      const diff = diffManifests(localManifest, cached.files);
      digest = diff.localDigest;
      if (diff.toUpload.length > 0 || diff.toDelete.length > 0) {
        const applied = await this.applyDelta(worktree, diff, sha256ManifestText, cached.digest, signal);
        if (applied.stale) {
          const res = await this.resyncClean(worktree, localManifest, sha256ManifestText, signal);
          if (res === null) {
            return { ok: false, error: "failed to heal stale remote manifest" };
          }
          uploaded = res.uploaded;
          deleted = res.deleted;
        } else if (!applied.ok) {
          const target = `${this.transport.spec.name} (${this.transport.spec.host})`;
          prefix(`remote sync to ${target} failed: ${sanitizeRemoteLogText(applied.error ?? "unknown")}`);
          return { ok: false, error: applied.error ?? "unknown" };
        } else {
          uploaded = diff.toUpload.length;
          deleted = diff.toDelete.length;
          saveRemoteManifestCache(this.remoteCachePath, localManifest);
        }
      }
    }

    const elapsedSec = ((performance.now() - syncStart) / 1000).toFixed(2);
    return { ok: true, localManifest, sha256ManifestText, digest, uploaded, deleted, elapsedSec };
  }
}
