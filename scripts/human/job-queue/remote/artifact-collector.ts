// Layer 3 Zero-Trust Output & Artifact Boundary:
//   1. `RemoteStreamSanitizer`: stateful UTF-8 + ANSI filter stripping OSC/DCS/cursor controls while keeping SGR.
//   2. `parseArtifactFrames`: single-pass parser for length-framed records (`<path>\0<bytes>\n<payload>`).
//   3. 6-Gate Chain of Responsibility (`ArtifactGates`): path hygiene, unconditional denylist, plugin/collect
//      allowlist, size/count quotas, symlink/junction realpath guard, and plain file write (never `tar -xf`).
//   4. `ArtifactCollector`: pulls failure artifacts, crash summaries (in memory), and collectPaths over transport.

import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as path from "node:path";
import { StringDecoder } from "node:string_decoder";
import { errorCode, errorMessage } from "../history.ts";
import type { RemoteCollectPathSpec, RemoteFailureArtifactRule } from "../plugin-api.ts";
import { prefix, resolveStateDirectory } from "../system.ts";
import type { ResolvedRemoteRules } from "./rules.ts";
import { bashQuote, bashWorkspaceExpr } from "./spec.ts";
import type { RemoteTransport } from "./transport.ts";

export type CollectPathSpec = RemoteCollectPathSpec;

export const MaxArtifactFiles = 128;
export const MaxSingleArtifactBytes = 16 * 1024 * 1024;
export const MaxTotalArtifactBytes = 32 * 1024 * 1024;
export const MaxControlSeqLen = 4096;
export const QuarantineFolderName = "remote-quarantine";
export const MaxQuarantineDirs = 200;
export const MaxQuarantineAgeMs = 7 * 24 * 60 * 60 * 1000;

type SanitizerState = "normal" | "esc" | "csi" | "osc" | "osc_esc" | "st_string" | "st_esc";

/**
 * Stateful terminal sanitizer for untrusted remote stdout/stderr chunks.
 * Preserves live UTF-8 test names, exceptions, stack traces, and SGR color codes while stripping OSC 52 clipboard
 * sequences, window title injection, cursor overwrites, and other terminal control escapes with O(1) memory.
 */
export class RemoteStreamSanitizer {
  private readonly decoder = new StringDecoder("utf8");
  private state: SanitizerState = "normal";
  private csiParams = "";
  private controlSeqLen = 0;

  push(chunk: Buffer | string): string {
    const text = typeof chunk === "string" ? chunk : this.decoder.write(chunk);
    return this.process(text);
  }

  flush(): string {
    const tail = this.decoder.end();
    const out = tail !== "" ? this.process(tail) : "";
    this.resetToNormal();
    return out;
  }

  private resetToNormal(): void {
    this.state = "normal";
    this.csiParams = "";
    this.controlSeqLen = 0;
  }

  private stepNormal(ch: string, code: number): string {
    if (code === 0x1b) {
      this.state = "esc";
      return "";
    }
    if (code === 0x0a || code === 0x0d || code === 0x09) {
      return ch;
    }
    if (code >= 0x20 && code !== 0x7f && (code < 0x80 || code > 0x9f)) {
      return ch;
    }
    return "";
  }

  private stepCsi(ch: string, code: number): string {
    if (code >= 0x40 && code <= 0x7e) {
      const sgr =
        ch === "m" && this.csiParams.length <= 32 && /^[0-9;]*$/.test(this.csiParams) ? `\x1b[${this.csiParams}m` : "";
      this.resetToNormal();
      return sgr;
    }
    if (code >= 0x20 && code <= 0x3f) {
      if (this.csiParams.length <= 33) {
        this.csiParams += ch;
      }
      return "";
    }
    this.resetToNormal();
    return "";
  }

  private stepOsc(ch: string, code: number): string {
    if (this.state === "osc_esc" && ch === "\\") {
      this.resetToNormal();
      return "";
    }
    if (code === 0x07 || code === 0x18 || code === 0x1a || code === 0x9c) {
      this.resetToNormal();
      return "";
    }
    if (code === 0x0a || code === 0x0d) {
      this.resetToNormal();
      return ch;
    }
    this.controlSeqLen++;
    if (this.controlSeqLen >= MaxControlSeqLen) {
      this.resetToNormal();
      return "";
    }
    this.state = code === 0x1b ? "osc_esc" : "osc";
    return "";
  }

  private stepStString(ch: string, code: number): void {
    if (this.state === "st_esc" && ch === "\\") {
      this.resetToNormal();
      return;
    }
    if (code === 0x18 || code === 0x1a || code === 0x9c) {
      this.resetToNormal();
      return;
    }
    this.controlSeqLen++;
    if (this.controlSeqLen >= MaxControlSeqLen) {
      this.resetToNormal();
      return;
    }
    this.state = code === 0x1b ? "st_esc" : "st_string";
  }

  private process(text: string): string {
    let out = "";
    for (let i = 0; i < text.length; i++) {
      const ch = text.charAt(i);
      const code = text.charCodeAt(i);
      if (this.state === "normal") {
        out += this.stepNormal(ch, code);
      } else if (this.state === "esc") {
        if (ch === "[") {
          this.state = "csi";
          this.csiParams = "";
        } else if (ch === "]") {
          this.state = "osc";
          this.controlSeqLen = 0;
        } else if (ch === "P" || ch === "X" || ch === "^" || ch === "_") {
          this.state = "st_string";
          this.controlSeqLen = 0;
        } else {
          this.resetToNormal();
        }
      } else if (this.state === "csi") {
        out += this.stepCsi(ch, code);
      } else if (this.state === "osc" || this.state === "osc_esc") {
        out += this.stepOsc(ch, code);
      } else {
        this.stepStString(ch, code);
      }
    }
    return out;
  }
}

/** Sanitizes untrusted remote text for single-line logs: strips OSC/CSI/SGR and escapes C0/DEL/C1 controls. */
export function sanitizeRemoteLogText(text: string): string {
  const san = new RemoteStreamSanitizer();
  const cleaned = san.push(text) + san.flush();
  return cleaned
    .replace(/\x1b\[[0-9;]*m/g, "")
    .replace(/[\x00-\x1f\x7f-\x9f]/g, (ch) => `\\u${ch.charCodeAt(0).toString(16).padStart(4, "0")}`);
}

/** One decoded length-framed record from the remote artifact stream. */
export interface ArtifactFrame {
  readonly relPosix: string;
  readonly data: Buffer;
}

export interface FrameParseResult {
  readonly frames: ArtifactFrame[];
  readonly errors: { path: string; reason: string }[];
}

/** Single-pass decoder for `<relPosix>\0<decimalByteLen>\n<rawBytes>` streams. */
export function parseArtifactFrames(
  payload: Buffer,
  maxFiles = MaxArtifactFiles,
  maxTotalBytes = MaxTotalArtifactBytes,
): FrameParseResult {
  const frames: ArtifactFrame[] = [];
  const errors: { path: string; reason: string }[] = [];
  if (payload.length === 0) {
    return { frames, errors };
  }
  if (payload.length > maxTotalBytes + maxFiles * 512) {
    errors.push({ path: "<stream>", reason: `payload exceeds total quota (${maxTotalBytes} bytes)` });
    return { frames, errors };
  }

  let offset = 0;
  while (offset < payload.length) {
    if (frames.length >= maxFiles) {
      errors.push({ path: "<stream>", reason: `exceeded max file count (${maxFiles})` });
      break;
    }
    const nulIndex = payload.indexOf(0x00, offset);
    if (nulIndex < 0) {
      errors.push({ path: "<stream>", reason: "malformed frame: missing path terminator" });
      break;
    }
    const relPosix = payload.subarray(offset, nulIndex).toString("utf8");
    const nlIndex = payload.indexOf(0x0a, nulIndex + 1);
    if (nlIndex < 0) {
      errors.push({ path: relPosix || "<stream>", reason: "malformed frame: missing size terminator" });
      break;
    }
    const sizeText = payload.subarray(nulIndex + 1, nlIndex).toString("ascii");
    if (!/^\d{1,12}$/.test(sizeText)) {
      errors.push({ path: relPosix, reason: `malformed frame size "${sizeText}"` });
      break;
    }
    const byteLen = Number(sizeText);
    const dataStart = nlIndex + 1;
    const dataEnd = dataStart + byteLen;
    if (!Number.isSafeInteger(byteLen) || byteLen < 0 || dataEnd > payload.length) {
      errors.push({ path: relPosix, reason: "truncated frame payload" });
      break;
    }
    frames.push({ relPosix, data: payload.subarray(dataStart, dataEnd) });
    offset = dataEnd;
  }
  return { frames, errors };
}

const ForbiddenPrefixes = [".git/", "scripts/", ".agents/", ".claude/", ".github/", ".vscode/", "node_modules/"];
const ForbiddenSegment = /(?:^|\/)(?:bin|obj|node_modules)\//i;
const VerifiedSnapshotPattern = /\.verified\.[^/]+$/i;
const SafeTextExtension = /[^/]\.(?:trx|cobertura\.xml|coverage\.xml|xml|log|txt|json|md|csv|tsv)$/i;
const ReceivedTextExtension = /[^/]\.received\.(?:txt|cs|cu|cpp|c|h|hpp|json|xml|md)$/i;
const SourceFilePattern = /\.(?:cs|cu|cpp|c|h|hpp)$/i;
const ReceivedSourcePattern = /[^/]\.received\.(?:cs|cu|cpp|c|h|hpp)$/i;
const WindowsReservedDevice = /^(?:con|prn|aux|nul|com[0-9\u00b2\u00b3\u00b9]|lpt[0-9\u00b2\u00b3\u00b9])(?:\.|$)/i;
const ZoneIdentifierContent = "[ZoneTransfer]\r\nZoneId=3\r\n";

function normKey(p: string): string {
  const resolved = path.resolve(p);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function isInsideRoot(root: string, target: string): boolean {
  const rel = path.relative(normKey(root), normKey(target));
  return (
    rel === "" || (rel !== ".." && !rel.startsWith(`..${path.sep}`) && !rel.startsWith("../") && !path.isAbsolute(rel))
  );
}

function normalizeTrackedFiles(knownTrackedFiles?: ReadonlySet<string>): ReadonlySet<string> | null {
  if (knownTrackedFiles === undefined) {
    return null;
  }
  const out = new Set<string>();
  for (const entry of knownTrackedFiles) {
    const clean = path.posix.normalize(entry.split("\\").join("/")).replace(/^\.\//, "");
    out.add(process.platform === "win32" ? clean.toLowerCase() : clean);
  }
  return out;
}

export type GateVerdict = { ok: true } | { ok: false; reason: string };

interface GateContext {
  readonly realQuarantine: string;
  readonly allowFailureArtifacts: boolean;
  readonly knownLocalDirs: ReadonlySet<string>;
  readonly knownTrackedFiles: ReadonlySet<string> | null;
  readonly collectPaths: readonly CollectPathSpec[];
  readonly failureArtifacts: readonly RemoteFailureArtifactRule[];
  readonly maxSingleBytes: number;
  readonly maxTotalBytes: number;
  readonly currentTotalBytes: number;
}

type ArtifactGate = (frame: ArtifactFrame, ctx: GateContext) => GateVerdict;

/** Gate 1: normalized relative POSIX path with no traversal, Windows device names, trailing dot/space, or controls. */
function gatePathHygiene(relPosix: string): GateVerdict {
  if (
    relPosix === "" ||
    relPosix.startsWith("/") ||
    relPosix.includes("\\") ||
    relPosix.includes(":") ||
    relPosix.includes("\0") ||
    /[\x00-\x1f\x7f]/.test(relPosix)
  ) {
    return { ok: false, reason: "invalid path characters" };
  }
  const normalized = path.posix.normalize(relPosix);
  if (normalized !== relPosix || normalized === "." || normalized === ".." || normalized.startsWith("../")) {
    return { ok: false, reason: "path traversal or non-normalized path" };
  }
  for (const seg of relPosix.split("/")) {
    if (/[. ]$/.test(seg) || WindowsReservedDevice.test(seg)) {
      return { ok: false, reason: `invalid Windows path segment "${seg}"` };
    }
  }
  return { ok: true };
}

/** Gate 2: unconditional deny list (repo metadata, scripts, tracked files, verified snapshots). */
function gateUnconditionalDeny(relPosix: string, trackedFiles: ReadonlySet<string> | null = null): GateVerdict {
  const lower = relPosix.toLowerCase();
  for (const p of ForbiddenPrefixes) {
    if (lower.startsWith(p)) {
      return { ok: false, reason: `forbidden prefix ${p}` };
    }
  }
  if (ForbiddenSegment.test(relPosix)) {
    return { ok: false, reason: "forbidden build/dependency segment" };
  }
  if (VerifiedSnapshotPattern.test(relPosix)) {
    return { ok: false, reason: "refusing to overwrite *.verified.* snapshot" };
  }
  if (trackedFiles !== null) {
    const key = process.platform === "win32" ? lower : relPosix;
    if (trackedFiles.has(key)) {
      return { ok: false, reason: "refusing to overwrite tracked repository file" };
    }
  }
  return { ok: true };
}

/** Gate 3: strict positive text-only allowlist (source-file guard, plugin failure artifact rules, or collectPaths). */
function gateAllowlist(
  relPosix: string,
  allowFailureArtifacts: boolean,
  knownLocalDirs: ReadonlySet<string>,
  collectPaths: readonly CollectPathSpec[],
  failureArtifacts: readonly RemoteFailureArtifactRule[],
): GateVerdict {
  if (SourceFilePattern.test(relPosix) && !ReceivedSourcePattern.test(relPosix)) {
    return { ok: false, reason: "source file write not allowed" };
  }
  if (VerifiedSnapshotPattern.test(relPosix)) {
    return { ok: false, reason: "refusing to overwrite *.verified.* snapshot" };
  }
  const baseName = relPosix.slice(relPosix.lastIndexOf("/") + 1);
  if (baseName.startsWith(".")) {
    return { ok: false, reason: "dotfile write not allowed" };
  }
  if (allowFailureArtifacts) {
    for (const rule of failureArtifacts) {
      if (rule.allowPattern.test(relPosix)) {
        if (rule.requireKnownLocalDir) {
          const dir = path.posix.dirname(relPosix);
          if (!knownLocalDirs.has(dir)) {
            return { ok: false, reason: `parent directory ${dir} does not exist in local manifest` };
          }
        }
        if (!SafeTextExtension.test(relPosix) && !ReceivedTextExtension.test(relPosix)) {
          return { ok: false, reason: "file extension not in text allowlist" };
        }
        return { ok: true };
      }
    }
  }
  for (const spec of collectPaths) {
    const normSpec = path.posix.normalize(spec.path).replace(/\/+$/, "");
    if (spec.kind === "file" && relPosix === normSpec && SafeTextExtension.test(relPosix)) {
      return { ok: true };
    }
    if (spec.kind === "dir" && relPosix.startsWith(`${normSpec}/`) && SafeTextExtension.test(relPosix)) {
      return { ok: true };
    }
  }
  return { ok: false, reason: "path not in explicit collectPaths or failure artifact allowlist" };
}

/** Gate 4: per-file and total byte quotas. */
function gateQuotas(frame: ArtifactFrame, ctx: GateContext): GateVerdict {
  if (frame.data.length > ctx.maxSingleBytes) {
    return { ok: false, reason: `exceeds single-file quota (${ctx.maxSingleBytes} bytes)` };
  }
  if (ctx.currentTotalBytes + frame.data.length > ctx.maxTotalBytes) {
    return { ok: false, reason: `exceeds total quota (${ctx.maxTotalBytes} bytes)` };
  }
  return { ok: true };
}

/** Gate 5: symlink / junction / non-regular-file traversal guard along every path segment inside quarantine. */
function gateSymlinkGuard(frame: ArtifactFrame, ctx: GateContext): GateVerdict {
  const segments = frame.relPosix.split("/");
  let cur = ctx.realQuarantine;
  for (let i = 0; i < segments.length; i++) {
    cur = path.join(cur, segments[i] as string);
    let st: fs.Stats | null = null;
    try {
      st = fs.lstatSync(cur);
    } catch (err) {
      if (err && typeof err === "object" && "code" in err && (err as { code?: string }).code === "ENOENT") {
        st = null;
      } else {
        return { ok: false, reason: `lstat failed on ${frame.relPosix}` };
      }
    }
    if (st !== null) {
      if (st.isSymbolicLink()) {
        return { ok: false, reason: `symlink/junction detected along path ${frame.relPosix}` };
      }
      const isLast = i === segments.length - 1;
      if (isLast && !st.isFile()) {
        return { ok: false, reason: `existing target is not a regular file: ${frame.relPosix}` };
      }
      if (!isLast && !st.isDirectory()) {
        return { ok: false, reason: `existing ancestor is not a directory: ${frame.relPosix}` };
      }
    }
  }
  return { ok: true };
}

/** Ordered Chain of Responsibility for validating every incoming artifact frame before disk write. */
const ArtifactGates: readonly ArtifactGate[] = [
  (frame) => gatePathHygiene(frame.relPosix),
  (frame, ctx) => gateUnconditionalDeny(frame.relPosix, ctx.knownTrackedFiles),
  (frame, ctx) =>
    gateAllowlist(
      frame.relPosix,
      ctx.allowFailureArtifacts,
      ctx.knownLocalDirs,
      ctx.collectPaths,
      ctx.failureArtifacts,
    ),
  gateQuotas,
  gateSymlinkGuard,
];

/** Checks whether a relative POSIX path passes Gates 1-3. */
export function isAllowedArtifactPath(
  relPosix: string,
  allowFailureArtifacts: boolean,
  knownLocalDirs: ReadonlySet<string>,
  collectPaths: readonly CollectPathSpec[],
  failureArtifacts: readonly RemoteFailureArtifactRule[] = [],
  knownTrackedFiles?: ReadonlySet<string>,
): GateVerdict {
  const g1 = gatePathHygiene(relPosix);
  if (!g1.ok) {
    return g1;
  }
  const g2 = gateUnconditionalDeny(relPosix, normalizeTrackedFiles(knownTrackedFiles));
  if (!g2.ok) {
    return g2;
  }
  return gateAllowlist(relPosix, allowFailureArtifacts, knownLocalDirs, collectPaths, failureArtifacts);
}

export interface ArtifactWriteOptions {
  quarantineDir: string;
  worktree?: string;
  stateDir?: string;
  allowReceived: boolean;
  knownLocalDirs: ReadonlySet<string>;
  knownTrackedFiles?: ReadonlySet<string>;
  collectPaths: readonly CollectPathSpec[];
  failureArtifacts?: readonly RemoteFailureArtifactRule[];
  maxFiles?: number;
  maxSingleBytes?: number;
  maxTotalBytes?: number;
  warn?: (message: string) => void;
  platform?: NodeJS.Platform;
  writeAds?: (adsPath: string, content: string) => void;
  chmod?: (targetPath: string, mode: number) => void;
}

export interface ArtifactWriteResult {
  quarantineDir: string;
  writtenFiles: string[];
  rejectedFiles: { path: string; reason: string }[];
  totalBytes: number;
  quarantineBlocked?: boolean;
}

/** Validates a job ID or segment for use as `<stateDir>/remote-quarantine/<jobId>` without path traversal. */
export function resolveQuarantineJobSegment(jobId?: number | string): string {
  if (jobId === undefined) {
    return `adhoc-${Date.now()}-${process.pid}-${crypto.randomUUID().slice(0, 8)}`;
  }
  if (typeof jobId === "number") {
    if (!Number.isSafeInteger(jobId) || jobId <= 0) {
      throw new Error(`invalid quarantine jobId: ${String(jobId)}`);
    }
    return String(jobId);
  }
  if (typeof jobId === "string") {
    const seg = jobId.trim();
    if (
      !/^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/.test(seg) ||
      WindowsReservedDevice.test(seg) ||
      (/^\d+$/.test(seg) && Number(seg) <= 0)
    ) {
      throw new Error(`invalid quarantine jobId segment: ${JSON.stringify(jobId)}`);
    }
    return seg;
  }
  throw new Error(`invalid quarantine jobId type: ${typeof jobId}`);
}

function lstatOrNull(targetPath: string): fs.Stats | null {
  try {
    return fs.lstatSync(targetPath);
  } catch (err) {
    if (errorCode(err) === "ENOENT") {
      return null;
    }
    throw err;
  }
}

function walkExistingSegments(rootDir: string, targetDir: string): string | null {
  const rel = path.relative(rootDir, targetDir);
  const segments = rel.split(path.sep).filter(Boolean);
  let cur = rootDir;
  for (const seg of segments) {
    cur = path.join(cur, seg);
    const st = lstatOrNull(cur);
    if (st === null) {
      break;
    }
    if (st.isSymbolicLink()) {
      return `symlink/junction detected at quarantine root: ${seg}`;
    }
    if (!st.isDirectory()) {
      return `quarantine root segment is not a directory: ${seg}`;
    }
  }
  return null;
}

function checkWorktreeContainmentAndAncestors(
  targetPath: string,
  worktreeLex: string,
  resolvedWorktree: string,
): string | null {
  if (
    normKey(targetPath) === normKey(resolvedWorktree) ||
    normKey(targetPath) === normKey(worktreeLex) ||
    isInsideRoot(targetPath, resolvedWorktree) ||
    isInsideRoot(targetPath, worktreeLex)
  ) {
    return "quarantine root overlaps or encloses worktree";
  }
  const worktreeWalkBase = isInsideRoot(worktreeLex, targetPath)
    ? worktreeLex
    : isInsideRoot(resolvedWorktree, targetPath)
      ? resolvedWorktree
      : null;
  if (worktreeWalkBase !== null) {
    return walkExistingSegments(worktreeWalkBase, targetPath);
  }
  let cur = targetPath;
  while (true) {
    const st = lstatOrNull(cur);
    if (st !== null) {
      const realExisting = fs.realpathSync.native(cur);
      if (normKey(realExisting) === normKey(resolvedWorktree) || isInsideRoot(resolvedWorktree, realExisting)) {
        return "quarantine root ancestor resolves inside worktree (symlink/junction)";
      }
      break;
    }
    const parent = path.dirname(cur);
    if (parent === cur) {
      break;
    }
    cur = parent;
  }
  return null;
}

function checkStateDirAncestors(targetPath: string, stateDir: string): string | null {
  const resolvedState = path.resolve(stateDir);
  if (!isInsideRoot(resolvedState, targetPath) || normKey(targetPath) === normKey(resolvedState)) {
    return "quarantine root escapes stateDir";
  }
  const stateSt = lstatOrNull(resolvedState);
  if (stateSt !== null) {
    if (stateSt.isSymbolicLink()) {
      return `symlink/junction detected at quarantine root: ${path.basename(resolvedState)}`;
    }
    if (!stateSt.isDirectory()) {
      return `quarantine root segment is not a directory: ${path.basename(resolvedState)}`;
    }
  }
  return walkExistingSegments(resolvedState, targetPath);
}

/**
 * Validates that `targetPath` does not traverse any symlink/junction ancestor below `worktree` or `stateDir`
 * (or resolve inside `worktree`), without creating directories.
 */
export function validateQuarantineRootAncestors(
  targetPath: string,
  stateDir?: string,
  worktree?: string,
): string | null {
  const resolvedTarget = path.resolve(targetPath);
  try {
    let resolvedWorktree: string | undefined;
    if (worktree !== undefined) {
      const worktreeLex = path.resolve(worktree);
      resolvedWorktree = fs.existsSync(worktreeLex) ? fs.realpathSync.native(worktreeLex) : worktreeLex;
      const wtErr = checkWorktreeContainmentAndAncestors(resolvedTarget, worktreeLex, resolvedWorktree);
      if (wtErr !== null) {
        return wtErr;
      }
    }
    if (stateDir !== undefined) {
      const stateErr = checkStateDirAncestors(resolvedTarget, stateDir);
      if (stateErr !== null) {
        return stateErr;
      }
    } else if (worktree === undefined || normKey(path.dirname(resolvedTarget)) !== normKey(path.resolve(worktree))) {
      const parentDir = path.dirname(resolvedTarget);
      const parentSt = lstatOrNull(parentDir);
      if (parentSt !== null) {
        if (parentSt.isSymbolicLink()) {
          return `symlink/junction detected at quarantine root: ${path.basename(parentDir)}`;
        }
        if (!parentSt.isDirectory()) {
          return `quarantine root is not a directory: ${path.basename(parentDir)}`;
        }
      }
    }
    const existingTarget = lstatOrNull(resolvedTarget);
    if (existingTarget !== null) {
      if (existingTarget.isSymbolicLink()) {
        return `symlink/junction detected at quarantine root: ${path.basename(resolvedTarget)}`;
      }
      if (!existingTarget.isDirectory()) {
        return `quarantine root is not a directory: ${path.basename(resolvedTarget)}`;
      }
      const realTarget = fs.realpathSync.native(resolvedTarget);
      if (stateDir !== undefined) {
        const realState = fs.realpathSync.native(path.resolve(stateDir));
        if (!isInsideRoot(realState, realTarget) || normKey(realTarget) === normKey(realState)) {
          return "quarantine root escapes stateDir";
        }
      }
      if (resolvedWorktree !== undefined) {
        if (normKey(realTarget) === normKey(resolvedWorktree) || isInsideRoot(realTarget, resolvedWorktree)) {
          return "quarantine root overlaps or encloses worktree";
        }
      }
    }
    return null;
  } catch (err) {
    return `failed to inspect quarantine root: ${errorMessage(err)}`;
  }
}

function prepareQuarantineRoot(
  quarantineTarget: string,
  stateDir?: string,
  worktreeLex?: string,
  resolvedWorktree?: string,
): { ok: true; realQuarantine: string } | { ok: false; reason: string } {
  try {
    if (worktreeLex !== undefined && resolvedWorktree !== undefined) {
      const wtErr = checkWorktreeContainmentAndAncestors(quarantineTarget, worktreeLex, resolvedWorktree);
      if (wtErr !== null) {
        return { ok: false, reason: wtErr };
      }
    }

    if (stateDir !== undefined) {
      const stateErr = checkStateDirAncestors(quarantineTarget, stateDir);
      if (stateErr !== null) {
        return { ok: false, reason: stateErr };
      }
    } else {
      const parentDir = path.dirname(quarantineTarget);
      for (const candidate of [parentDir, quarantineTarget]) {
        const st = lstatOrNull(candidate);
        if (st !== null) {
          if (st.isSymbolicLink()) {
            return { ok: false, reason: `symlink/junction detected at quarantine root: ${path.basename(candidate)}` };
          }
          if (!st.isDirectory()) {
            return { ok: false, reason: `quarantine root is not a directory: ${path.basename(candidate)}` };
          }
        }
      }
    }
    // A folder left under the same job id (an id reused after the state was reset) is emptied before the first
    // write, so that none of its files reads as this run's; a link inside is removed, never followed
    const existing = lstatOrNull(quarantineTarget);
    if (existing !== null) {
      if (existing.isSymbolicLink() || !existing.isDirectory()) {
        return { ok: false, reason: `quarantine root is not a directory: ${path.basename(quarantineTarget)}` };
      }
      for (const entry of fs.readdirSync(quarantineTarget)) {
        removeReadOnlyTree(path.join(quarantineTarget, entry));
      }
    }
    fs.mkdirSync(quarantineTarget, { recursive: true });
    const realQuarantine = fs.realpathSync.native(quarantineTarget);
    if (stateDir !== undefined) {
      const realState = fs.realpathSync.native(stateDir);
      if (!isInsideRoot(realState, realQuarantine) || normKey(realQuarantine) === normKey(realState)) {
        return { ok: false, reason: "quarantine root escapes stateDir" };
      }
    }
    if (resolvedWorktree !== undefined) {
      if (normKey(realQuarantine) === normKey(resolvedWorktree) || isInsideRoot(realQuarantine, resolvedWorktree)) {
        return { ok: false, reason: "quarantine root overlaps or encloses worktree" };
      }
    }
    return { ok: true, realQuarantine };
  } catch (err) {
    return { ok: false, reason: `failed to prepare quarantine root: ${errorMessage(err)}` };
  }
}

function validatePreRootGates(frame: ArtifactFrame, ctx: GateContext, seenLower: Set<string>): string | null {
  for (let i = 0; i < 4; i++) {
    const gate = ArtifactGates[i] as ArtifactGate;
    const verdict = gate(frame, ctx);
    if (!verdict.ok) {
      return verdict.reason;
    }
  }
  if (seenLower.has(frame.relPosix.toLowerCase())) {
    return "case-colliding or duplicate artifact path";
  }
  return null;
}

/** Runs decoded frames through the 6-gate chain and writes approved text artifacts into `quarantineDir` only. */
export function writeValidatedFrames(
  frames: readonly ArtifactFrame[],
  options: ArtifactWriteOptions,
): ArtifactWriteResult {
  if (typeof options.quarantineDir !== "string" || options.quarantineDir.trim() === "") {
    throw new Error("quarantineDir is required");
  }
  const quarantineTarget = path.resolve(options.quarantineDir);
  const worktreeLex = options.worktree !== undefined ? path.resolve(options.worktree) : undefined;
  const resolvedWorktree =
    worktreeLex !== undefined
      ? fs.existsSync(worktreeLex)
        ? fs.realpathSync.native(worktreeLex)
        : worktreeLex
      : undefined;
  if (
    resolvedWorktree !== undefined &&
    worktreeLex !== undefined &&
    (normKey(quarantineTarget) === normKey(resolvedWorktree) ||
      normKey(quarantineTarget) === normKey(worktreeLex) ||
      isInsideRoot(quarantineTarget, resolvedWorktree) ||
      isInsideRoot(quarantineTarget, worktreeLex))
  ) {
    throw new Error("quarantineDir must be separate from worktree and must not enclose worktree");
  }
  if (options.stateDir !== undefined) {
    const resolvedState = path.resolve(options.stateDir);
    if (!isInsideRoot(resolvedState, quarantineTarget) || normKey(quarantineTarget) === normKey(resolvedState)) {
      throw new Error("quarantineDir must lie inside stateDir");
    }
  }

  const maxSingleBytes = options.maxSingleBytes ?? MaxSingleArtifactBytes;
  const maxTotalBytes = options.maxTotalBytes ?? MaxTotalArtifactBytes;
  const warn = options.warn ?? (() => {});
  const targetPlatform = options.platform ?? process.platform;
  const chmod = options.chmod ?? fs.chmodSync;
  const writtenFiles: string[] = [];
  const rejectedFiles: { path: string; reason: string }[] = [];
  let totalBytes = 0;

  if (frames.length === 0) {
    return { quarantineDir: quarantineTarget, writtenFiles, rejectedFiles, totalBytes };
  }

  const knownTrackedFiles = normalizeTrackedFiles(options.knownTrackedFiles);
  const seenLower = new Set<string>();
  let realQuarantine: string | null = null;
  let rootError: string | null = null;

  for (const frame of frames) {
    const preCtx: GateContext = {
      realQuarantine: "",
      allowFailureArtifacts: options.allowReceived,
      knownLocalDirs: options.knownLocalDirs,
      knownTrackedFiles,
      collectPaths: options.collectPaths,
      failureArtifacts: options.failureArtifacts ?? [],
      maxSingleBytes,
      maxTotalBytes,
      currentTotalBytes: totalBytes,
    };

    const preRejected = validatePreRootGates(frame, preCtx, seenLower);
    if (preRejected !== null) {
      rejectedFiles.push({ path: frame.relPosix, reason: preRejected });
      warn(`rejected remote artifact ${sanitizeRemoteLogText(frame.relPosix)}: ${sanitizeRemoteLogText(preRejected)}`);
      continue;
    }

    // Create quarantineDir lazily only once a frame has passed Gates 1-4 and duplicate checks.
    if (realQuarantine === null && rootError === null) {
      const prepared = prepareQuarantineRoot(quarantineTarget, options.stateDir, worktreeLex, resolvedWorktree);
      if (prepared.ok) {
        realQuarantine = prepared.realQuarantine;
      } else {
        rootError = prepared.reason;
      }
    }
    if (rootError !== null || realQuarantine === null) {
      const reason = rootError ?? "quarantine root unavailable";
      rejectedFiles.push({ path: frame.relPosix, reason });
      warn(`rejected remote artifact ${sanitizeRemoteLogText(frame.relPosix)}: ${sanitizeRemoteLogText(reason)}`);
      continue;
    }

    const symlinkVerdict = gateSymlinkGuard(frame, { ...preCtx, realQuarantine });
    if (!symlinkVerdict.ok) {
      rejectedFiles.push({ path: frame.relPosix, reason: symlinkVerdict.reason });
      warn(
        `rejected remote artifact ${sanitizeRemoteLogText(frame.relPosix)}: ` +
          `${sanitizeRemoteLogText(symlinkVerdict.reason)}`,
      );
      continue;
    }

    const targetPath = path.join(realQuarantine, ...frame.relPosix.split("/"));
    const parentDir = path.dirname(targetPath);
    try {
      fs.mkdirSync(parentDir, { recursive: true });
      const realParent = fs.realpathSync.native(parentDir);
      if (!isInsideRoot(realQuarantine, realParent)) {
        rejectedFiles.push({ path: frame.relPosix, reason: "resolved parent escapes quarantine" });
        warn(`rejected remote artifact ${sanitizeRemoteLogText(frame.relPosix)}: parent escapes quarantine`);
        continue;
      }
    } catch (err) {
      const reason = `failed to create quarantine parent directory: ${errorMessage(err)}`;
      rejectedFiles.push({ path: frame.relPosix, reason });
      warn(`rejected remote artifact ${sanitizeRemoteLogText(frame.relPosix)}: ${sanitizeRemoteLogText(reason)}`);
      continue;
    }

    seenLower.add(frame.relPosix.toLowerCase());
    // Gate 6: plain file write (never tar, never chmod +x), Windows Mark of the Web before 0o444 read-only
    try {
      try {
        fs.writeFileSync(targetPath, frame.data, { mode: 0o644, flag: "wx" });
      } catch (writeErr) {
        if (errorCode(writeErr) !== "EEXIST") {
          throw writeErr;
        }
        const existing = fs.lstatSync(targetPath);
        if (existing.isSymbolicLink() || !existing.isFile()) {
          throw new Error("existing target is not a regular file");
        }
        chmod(targetPath, 0o644);
        fs.writeFileSync(targetPath, frame.data, { mode: 0o644 });
      }
    } catch (err) {
      const reason = `quarantine write failed: ${errorMessage(err)}`;
      rejectedFiles.push({ path: frame.relPosix, reason });
      warn(`rejected remote artifact ${sanitizeRemoteLogText(frame.relPosix)}: ${sanitizeRemoteLogText(reason)}`);
      continue;
    }

    if (targetPlatform === "win32") {
      const adsPath = `${targetPath}:Zone.Identifier`;
      try {
        if (options.writeAds !== undefined) {
          options.writeAds(adsPath, ZoneIdentifierContent);
        } else {
          fs.writeFileSync(adsPath, ZoneIdentifierContent, "utf8");
        }
      } catch (err) {
        warn(
          `warning: failed to attach Zone.Identifier to ${sanitizeRemoteLogText(frame.relPosix)}: ` +
            `${sanitizeRemoteLogText(errorMessage(err))}`,
        );
      }
    }

    try {
      chmod(targetPath, 0o444);
    } catch (err) {
      try {
        fs.unlinkSync(targetPath);
      } catch {
        // Best-effort cleanup so a failed chmod does not leave an unlocked 0o644 file in quarantine.
      }
      const reason = `failed to set read-only permissions on quarantined artifact: ${errorMessage(err)}`;
      rejectedFiles.push({ path: frame.relPosix, reason });
      warn(`rejected remote artifact ${sanitizeRemoteLogText(frame.relPosix)}: ${sanitizeRemoteLogText(reason)}`);
      continue;
    }

    writtenFiles.push(frame.relPosix);
    totalBytes += frame.data.length;
  }

  return {
    quarantineDir: realQuarantine ?? quarantineTarget,
    writtenFiles,
    rejectedFiles,
    totalBytes,
    ...(rootError !== null ? { quarantineBlocked: true } : {}),
  };
}

/**
 * Decodes length-framed records from `payload` in a single pass and writes only those passing all 6 security gates.
 */
export function validateAndWriteArtifacts(payload: Buffer, options: ArtifactWriteOptions): ArtifactWriteResult {
  const maxFiles = options.maxFiles ?? MaxArtifactFiles;
  const maxTotalBytes = options.maxTotalBytes ?? MaxTotalArtifactBytes;
  const warn = options.warn ?? (() => {});
  const parsed = parseArtifactFrames(payload, maxFiles, maxTotalBytes);
  for (const err of parsed.errors) {
    warn(`remote artifact stream rejected (${sanitizeRemoteLogText(err.path)}): ${sanitizeRemoteLogText(err.reason)}`);
  }
  const result = writeValidatedFrames(parsed.frames, options);
  result.rejectedFiles.unshift(...parsed.errors);
  return result;
}

/** Recursively removes a directory tree or file, clearing read-only permissions first without following symlinks. */
export function removeReadOnlyTree(targetPath: string): void {
  const st = lstatOrNull(targetPath);
  if (st === null) {
    return;
  }
  if (st.isSymbolicLink()) {
    try {
      fs.unlinkSync(targetPath);
    } catch (err) {
      const code = errorCode(err);
      if (code === "ENOENT") {
        return;
      }
      if (code === "EPERM" || code === "EISDIR") {
        fs.rmdirSync(targetPath);
      } else {
        throw err;
      }
    }
    return;
  }
  if (st.isDirectory()) {
    try {
      fs.chmodSync(targetPath, 0o700);
    } catch {
      // Best-effort permission clear before reading/removing children.
    }
    for (const entry of fs.readdirSync(targetPath, { withFileTypes: true })) {
      removeReadOnlyTree(path.join(targetPath, entry.name));
    }
    fs.rmdirSync(targetPath);
    return;
  }
  try {
    fs.chmodSync(targetPath, 0o600);
  } catch {
    // Best-effort permission clear before unlinking file.
  }
  fs.unlinkSync(targetPath);
}

export interface PruneQuarantineOptions {
  readonly maxDirs?: number;
  readonly maxAgeMs?: number;
  readonly excludeName?: string;
  readonly now?: number;
  readonly warn?: (message: string) => void;
  readonly worktree?: string;
  readonly stateDir?: string;
}

export interface PruneQuarantineResult {
  readonly removed: string[];
  readonly kept: string[];
}

/**
 * Prunes the per-job subdirectories under `quarantineRoot` that are neither among the newest `maxDirs` nor within
 * `maxAgeMs`, clearing read-only bits before removal and skipping symlinks/junctions (including symlinked ancestors).
 */
export function pruneQuarantineDirs(
  quarantineRoot: string,
  options: PruneQuarantineOptions = {},
): PruneQuarantineResult {
  const maxDirs = options.maxDirs ?? MaxQuarantineDirs;
  const maxAgeMs = options.maxAgeMs ?? MaxQuarantineAgeMs;
  const excludeName = options.excludeName;
  const now = options.now ?? Date.now();
  const warn = options.warn ?? (() => {});

  const resolvedRoot = path.resolve(quarantineRoot);
  let resolvedWorktree: string | undefined;
  let rootStat: fs.Stats | null = null;
  try {
    if (options.worktree !== undefined) {
      const worktreeLex = path.resolve(options.worktree);
      resolvedWorktree = fs.existsSync(worktreeLex) ? fs.realpathSync.native(worktreeLex) : worktreeLex;
      const wtErr = checkWorktreeContainmentAndAncestors(resolvedRoot, worktreeLex, resolvedWorktree);
      if (wtErr !== null) {
        warn(`warning: skipping quarantine pruning (${wtErr}): ${sanitizeRemoteLogText(quarantineRoot)}`);
        return { removed: [], kept: [] };
      }
    }

    if (options.stateDir !== undefined) {
      const stateErr = checkStateDirAncestors(resolvedRoot, options.stateDir);
      if (stateErr !== null) {
        warn(`warning: skipping quarantine pruning (${stateErr}): ${sanitizeRemoteLogText(quarantineRoot)}`);
        return { removed: [], kept: [] };
      }
    } else if (
      options.worktree === undefined ||
      normKey(path.dirname(resolvedRoot)) !== normKey(path.resolve(options.worktree))
    ) {
      const parentDir = path.dirname(resolvedRoot);
      const parentSt = lstatOrNull(parentDir);
      if (parentSt !== null) {
        if (parentSt.isSymbolicLink()) {
          warn(
            `warning: skipping quarantine pruning (symlink/junction detected at quarantine parent: ` +
              `${sanitizeRemoteLogText(path.basename(parentDir))}): ${sanitizeRemoteLogText(quarantineRoot)}`,
          );
          return { removed: [], kept: [] };
        }
        if (!parentSt.isDirectory()) {
          warn(
            `warning: skipping quarantine pruning (quarantine parent is not a directory: ` +
              `${sanitizeRemoteLogText(path.basename(parentDir))}): ${sanitizeRemoteLogText(quarantineRoot)}`,
          );
          return { removed: [], kept: [] };
        }
      }
    }

    rootStat = lstatOrNull(resolvedRoot);
    if (rootStat === null) {
      return { removed: [], kept: [] };
    }
    if (rootStat.isSymbolicLink() || !rootStat.isDirectory()) {
      warn(
        `warning: skipping quarantine pruning because root is not a regular directory: ${sanitizeRemoteLogText(quarantineRoot)}`,
      );
      return { removed: [], kept: [] };
    }

    const realRoot = fs.realpathSync.native(resolvedRoot);
    if (options.stateDir !== undefined) {
      const realState = fs.realpathSync.native(path.resolve(options.stateDir));
      if (!isInsideRoot(realState, realRoot) || normKey(realRoot) === normKey(realState)) {
        warn(
          `warning: skipping quarantine pruning (quarantine root escapes stateDir): ${sanitizeRemoteLogText(quarantineRoot)}`,
        );
        return { removed: [], kept: [] };
      }
    }
    if (resolvedWorktree !== undefined) {
      if (normKey(realRoot) === normKey(resolvedWorktree) || isInsideRoot(realRoot, resolvedWorktree)) {
        warn(
          `warning: skipping quarantine pruning (quarantine root overlaps or encloses worktree): ${sanitizeRemoteLogText(quarantineRoot)}`,
        );
        return { removed: [], kept: [] };
      }
    }
  } catch (err) {
    warn(`warning: could not inspect quarantine root ${sanitizeRemoteLogText(quarantineRoot)}: ${errorMessage(err)}`);
    return { removed: [], kept: [] };
  }

  let entries: fs.Dirent[];
  try {
    entries = fs.readdirSync(quarantineRoot, { withFileTypes: true });
  } catch (err) {
    warn(`warning: could not list quarantine root ${sanitizeRemoteLogText(quarantineRoot)}: ${errorMessage(err)}`);
    return { removed: [], kept: [] };
  }

  interface Candidate {
    name: string;
    fullPath: string;
    mtimeMs: number;
    numId: number;
  }
  const candidates: Candidate[] = [];
  for (const entry of entries) {
    if (entry.isSymbolicLink()) {
      continue;
    }
    const fullPath = path.join(quarantineRoot, entry.name);
    let st: fs.Stats | null = null;
    try {
      st = lstatOrNull(fullPath);
    } catch {
      continue;
    }
    if (st === null || st.isSymbolicLink() || !st.isDirectory()) {
      continue;
    }
    candidates.push({
      name: entry.name,
      fullPath,
      mtimeMs: st.mtimeMs,
      numId: /^\d+$/.test(entry.name) ? Number(entry.name) : Number.MAX_SAFE_INTEGER,
    });
  }

  // Newest first: a folder stays while it is among the newest maxDirs or not older than maxAgeMs (either keeps it;
  // a negative limit keeps everything it would decide)
  candidates.sort((a, b) => b.mtimeMs - a.mtimeMs || b.numId - a.numId || b.name.localeCompare(a.name));

  const removed: string[] = [];
  const kept: string[] = [];
  candidates.forEach((candidate, rank) => {
    const recent = maxDirs < 0 || rank < maxDirs;
    const young = maxAgeMs < 0 || now - candidate.mtimeMs <= maxAgeMs;
    if (candidate.name === excludeName || recent || young) {
      kept.push(candidate.name);
      return;
    }
    try {
      removeReadOnlyTree(candidate.fullPath);
      removed.push(candidate.name);
    } catch (err) {
      warn(
        `warning: failed to prune quarantine directory ${sanitizeRemoteLogText(candidate.name)}: ` +
          `${sanitizeRemoteLogText(errorMessage(err))}`,
      );
      kept.push(candidate.name);
    }
  });

  return { removed, kept };
}

function toDotRel(relPath: string): string {
  const stripped = relPath.replace(/^(?:\.\/)+/, "");
  return stripped === "" ? "." : `./${stripped}`;
}

/** Builds the remote bash script that streams matching failure artifacts, crash summaries, and collectPaths. */
export function buildArtifactPullScript(
  workspace: string,
  allowFailureArtifacts: boolean,
  collectPaths: readonly CollectPathSpec[],
  remoteRules: ResolvedRemoteRules,
): string | null {
  const collectLines = collectPaths.map((spec) => {
    const q = bashQuote(toDotRel(spec.path));
    return spec.kind === "file"
      ? `if [ -f ${q} ] && [ ! -L ${q} ]; then printf '%s\\0' ${q}; fi`
      : `if [ -d ${q} ] && [ ! -L ${q} ]; then ` +
          `find -- ${q} -type f ! -type l ! -name '*.dmp' ! -name '*.gcdump' ! -name '*.binlog' -print0 2>/dev/null || true; fi`;
  });
  const failureFindLines: string[] = [];
  if (allowFailureArtifacts) {
    for (const rule of [...remoteRules.failureArtifacts, ...remoteRules.crashSummaries]) {
      if (rule.searchDirs.length > 0) {
        const dirs = rule.searchDirs.map((d) => bashQuote(toDotRel(d))).join(" ");
        failureFindLines.push(
          `find -- ${dirs} -type f ! -type l -name ${bashQuote(rule.findName)} -print0 2>/dev/null || true`,
        );
      }
    }
  }
  if (failureFindLines.length === 0 && collectLines.length === 0) {
    return null;
  }
  return [
    "set -euo pipefail",
    `WS=${bashWorkspaceExpr(workspace)}`,
    `cd "$WS"`,
    "emit_file() {",
    '  local f="${1#./}"',
    '  local p="./$f"',
    '  [ -f "$p" ] && [ ! -L "$p" ] || return 0',
    '  local sz=$(stat -c %s -- "$p" 2>/dev/null || echo -1)',
    '  [ "$sz" -ge 0 ] || return 0',
    '  printf "%s\\0%s\\n" "$f" "$sz"',
    '  cat -- "$p"',
    "}",
    "{",
    ...failureFindLines.map((l) => `  ${l}`),
    ...collectLines.map((l) => `  ${l}`),
    "} | while IFS= read -r -d '' f; do emit_file \"$f\"; done",
  ].join("\n");
}

export interface PullArtifactsOptions {
  readonly knownTrackedFiles?: ReadonlySet<string>;
  readonly warn?: (message: string) => void;
  readonly signal?: AbortSignal;
  readonly quarantineDir?: string;
  readonly stateDir?: string;
  readonly jobId?: number | string;
  readonly platform?: NodeJS.Platform;
  readonly writeAds?: (adsPath: string, content: string) => void;
}

export interface PullArtifactsResult {
  quarantineDir: string;
  written: number;
  rejected: number;
  bytes: number;
  crashedTests: string[];
  quarantineBlocked?: boolean;
}

function resolvePullOptions(optionsOrTracked?: PullArtifactsOptions | ReadonlySet<string>): {
  knownTrackedFiles: ReadonlySet<string> | undefined;
  warn: (message: string) => void;
  signal: AbortSignal | undefined;
  quarantineDir: string | undefined;
  stateDir: string | undefined;
  jobId: number | string | undefined;
  platform: NodeJS.Platform | undefined;
  writeAds: ((adsPath: string, content: string) => void) | undefined;
} {
  if (optionsOrTracked === undefined) {
    return {
      knownTrackedFiles: undefined,
      warn: prefix,
      signal: undefined,
      quarantineDir: undefined,
      stateDir: undefined,
      jobId: undefined,
      platform: undefined,
      writeAds: undefined,
    };
  }
  if (typeof (optionsOrTracked as ReadonlySet<string>).has === "function") {
    return {
      knownTrackedFiles: optionsOrTracked as ReadonlySet<string>,
      warn: prefix,
      signal: undefined,
      quarantineDir: undefined,
      stateDir: undefined,
      jobId: undefined,
      platform: undefined,
      writeAds: undefined,
    };
  }
  const opts = optionsOrTracked as PullArtifactsOptions;
  return {
    knownTrackedFiles: opts.knownTrackedFiles,
    warn: opts.warn ?? prefix,
    signal: opts.signal,
    quarantineDir: opts.quarantineDir,
    stateDir: opts.stateDir,
    jobId: opts.jobId,
    platform: opts.platform,
    writeAds: opts.writeAds,
  };
}

/** Pulls and validates remote failure artifacts + in-memory crash summaries into quarantine in a single pass. */
export class ArtifactCollector {
  private readonly transport: RemoteTransport;
  private readonly remoteRules: ResolvedRemoteRules;

  constructor(transport: RemoteTransport, remoteRules: ResolvedRemoteRules) {
    this.transport = transport;
    this.remoteRules = remoteRules;
  }

  async pull(
    worktree: string,
    allowFailureArtifacts: boolean,
    knownLocalDirs: ReadonlySet<string>,
    collectPaths: readonly CollectPathSpec[],
    optionsOrTracked?: PullArtifactsOptions | ReadonlySet<string>,
  ): Promise<PullArtifactsResult> {
    const { knownTrackedFiles, warn, signal, quarantineDir, stateDir, jobId, platform, writeAds } =
      resolvePullOptions(optionsOrTracked);
    const resolvedQuarantineDir =
      quarantineDir !== undefined
        ? path.resolve(quarantineDir)
        : path.join(stateDir ?? resolveStateDirectory(), QuarantineFolderName, resolveQuarantineJobSegment(jobId));
    if (signal?.aborted) {
      return { quarantineDir: resolvedQuarantineDir, written: 0, rejected: 0, bytes: 0, crashedTests: [] };
    }
    const script = buildArtifactPullScript(
      this.transport.spec.workspace,
      allowFailureArtifacts,
      collectPaths,
      this.remoteRules,
    );
    if (script === null) {
      return { quarantineDir: resolvedQuarantineDir, written: 0, rejected: 0, bytes: 0, crashedTests: [] };
    }
    const payload = await this.transport.execBinary(script, MaxTotalArtifactBytes + 1024 * 1024, signal);
    if (signal?.aborted || payload === null || payload.length === 0) {
      return { quarantineDir: resolvedQuarantineDir, written: 0, rejected: 0, bytes: 0, crashedTests: [] };
    }
    const parsed = parseArtifactFrames(payload);
    for (const err of parsed.errors) {
      warn(
        `remote artifact stream rejected (${sanitizeRemoteLogText(err.path)}): ${sanitizeRemoteLogText(err.reason)}`,
      );
    }

    const crashedTests: string[] = [];
    const artifactFrames: ArtifactFrame[] = [];
    const crashRejected: { path: string; reason: string }[] = [];
    let crashTotalBytes = 0;

    for (const frame of parsed.frames) {
      const crashRule = allowFailureArtifacts
        ? this.remoteRules.crashSummaries.find((rule) => rule.pathPattern.test(frame.relPosix))
        : undefined;
      if (crashRule === undefined) {
        artifactFrames.push(frame);
        continue;
      }
      const hygiene = gatePathHygiene(frame.relPosix);
      if (!hygiene.ok) {
        crashRejected.push({ path: frame.relPosix, reason: hygiene.reason });
        warn(
          `rejected remote crash summary ${sanitizeRemoteLogText(frame.relPosix)}: ` +
            `${sanitizeRemoteLogText(hygiene.reason)}`,
        );
        continue;
      }
      const quota = gateQuotas(frame, {
        realQuarantine: "",
        allowFailureArtifacts,
        knownLocalDirs,
        knownTrackedFiles: null,
        collectPaths,
        failureArtifacts: [],
        maxSingleBytes: MaxSingleArtifactBytes,
        maxTotalBytes: MaxTotalArtifactBytes,
        currentTotalBytes: crashTotalBytes,
      });
      if (!quota.ok) {
        crashRejected.push({ path: frame.relPosix, reason: quota.reason });
        warn(
          `rejected remote crash summary ${sanitizeRemoteLogText(frame.relPosix)}: ` +
            `${sanitizeRemoteLogText(quota.reason)}`,
        );
        continue;
      }
      crashTotalBytes += frame.data.length;
      crashedTests.push(...crashRule.parse(frame.data.toString("utf8")));
    }

    const writeResult = writeValidatedFrames(artifactFrames, {
      quarantineDir: resolvedQuarantineDir,
      worktree,
      stateDir,
      allowReceived: allowFailureArtifacts,
      knownLocalDirs,
      knownTrackedFiles,
      collectPaths,
      failureArtifacts: this.remoteRules.failureArtifacts,
      warn,
      platform,
      writeAds,
    });
    writeResult.rejectedFiles.unshift(...parsed.errors, ...crashRejected);

    return {
      quarantineDir: writeResult.quarantineDir,
      written: writeResult.writtenFiles.length,
      rejected: writeResult.rejectedFiles.length,
      bytes: writeResult.totalBytes,
      crashedTests: [...new Set(crashedTests)],
      ...(writeResult.quarantineBlocked ? { quarantineBlocked: true } : {}),
    };
  }
}
