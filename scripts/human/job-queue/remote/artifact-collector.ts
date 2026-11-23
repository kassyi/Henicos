// Layer 3 Zero-Trust Output & Artifact Boundary:
//   1. `RemoteStreamSanitizer`: stateful UTF-8 + ANSI filter stripping OSC/DCS/cursor controls while keeping SGR colors.
//   2. `parseArtifactFrames`: single-pass parser for length-framed records (`<path>\0<bytes>\n<payload>`).
//   3. 6-Gate Chain of Responsibility (`ArtifactGates`): path hygiene, unconditional denylist, plugin/collect allowlist,
//      size/count quotas, symlink/junction realpath guard, and plain file write (never `tar -xf`).
//   4. `ArtifactCollector`: pulls failure artifacts, crash summaries (in memory), and collectPaths over `RemoteTransport`.

import * as fs from "node:fs";
import * as path from "node:path";
import { StringDecoder } from "node:string_decoder";
import type { RemoteFailureArtifactRule } from "../plugin-api.ts";
import { prefix } from "../system.ts";
import type { ResolvedRemoteRules } from "./rules.ts";
import { bashQuote, bashWorkspaceExpr } from "./spec.ts";
import type { RemoteTransport } from "./transport.ts";

export interface CollectPathSpec {
  /** Worktree-relative normalized POSIX path (no leading/trailing slash). */
  path: string;
  /** "file" for exact redirection target (`> tmp/out.log`); "dir" for collected directories. */
  kind: "file" | "dir";
}

export const MaxArtifactFiles = 128;
export const MaxSingleArtifactBytes = 16 * 1024 * 1024;
export const MaxTotalArtifactBytes = 32 * 1024 * 1024;

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

  push(chunk: Buffer | string): string {
    const text = typeof chunk === "string" ? chunk : this.decoder.write(chunk);
    return this.process(text);
  }

  flush(): string {
    const tail = this.decoder.end();
    const out = tail !== "" ? this.process(tail) : "";
    this.state = "normal";
    this.csiParams = "";
    return out;
  }

  private process(text: string): string {
    let out = "";
    for (let i = 0; i < text.length; i++) {
      const ch = text.charAt(i);
      const code = text.charCodeAt(i);

      switch (this.state) {
        case "normal":
          if (code === 0x1b) {
            this.state = "esc";
          } else if (code === 0x0a || code === 0x0d || code === 0x09) {
            out += ch;
          } else if (code >= 0x20 && code !== 0x7f && (code < 0x80 || code > 0x9f)) {
            out += ch;
          }
          break;

        case "esc":
          if (ch === "[") {
            this.state = "csi";
            this.csiParams = "";
          } else if (ch === "]") {
            this.state = "osc";
          } else if (ch === "P" || ch === "X" || ch === "^" || ch === "_") {
            this.state = "st_string";
          } else {
            this.state = "normal";
          }
          break;

        case "csi":
          if (code >= 0x40 && code <= 0x7e) {
            if (ch === "m" && this.csiParams.length <= 32 && /^[0-9;]*$/.test(this.csiParams)) {
              out += `\x1b[${this.csiParams}m`;
            }
            this.csiParams = "";
            this.state = "normal";
          } else if (code >= 0x20 && code <= 0x3f) {
            if (this.csiParams.length <= 33) {
              this.csiParams += ch;
            }
          } else {
            this.csiParams = "";
            this.state = "normal";
          }
          break;

        case "osc":
          if (code === 0x07) {
            this.state = "normal";
          } else if (code === 0x1b) {
            this.state = "osc_esc";
          }
          break;

        case "osc_esc":
          this.state = ch === "\\" ? "normal" : code === 0x1b ? "osc_esc" : "osc";
          break;

        case "st_string":
          if (code === 0x1b) {
            this.state = "st_esc";
          }
          break;

        case "st_esc":
          this.state = ch === "\\" ? "normal" : code === 0x1b ? "st_esc" : "st_string";
          break;
      }
    }
    return out;
  }
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

const ForbiddenPrefixes = [".git/", "scripts/", ".agents/", ".claude/", "node_modules/"];
const ForbiddenSegment = /(?:^|\/)(?:bin|obj|node_modules)\//;
const VerifiedSnapshotPattern = /\.verified\.[^/]+$/i;
const ForbiddenExtension =
  /\.(?:csproj|fsproj|vbproj|props|targets|sln|slnx|slnf|js|mjs|cjs|ts|mts|cts|ps1|psm1|sh|bash|cmd|bat|exe|dll|so|dylib|dmp|pdb|reg|vbs|wsf|hta)$/i;
const SafeCollectExtension = /\.(?:trx|xml|json|log|txt|md|html|csv|tsv)$/i;

function normKey(p: string): string {
  const resolved = path.resolve(p);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function isInsideRoot(root: string, target: string): boolean {
  const rel = path.relative(normKey(root), normKey(target));
  return rel === "" || (!rel.startsWith("..") && !path.isAbsolute(rel));
}

export type GateVerdict = { ok: true } | { ok: false; reason: string };

interface GateContext {
  readonly realWorktree: string;
  readonly allowFailureArtifacts: boolean;
  readonly knownLocalDirs: ReadonlySet<string>;
  readonly collectPaths: readonly CollectPathSpec[];
  readonly failureArtifacts: readonly RemoteFailureArtifactRule[];
  readonly maxSingleBytes: number;
  readonly maxTotalBytes: number;
  readonly currentTotalBytes: number;
}

type ArtifactGate = (frame: ArtifactFrame, ctx: GateContext) => GateVerdict;

/** Gate 1: normalized relative POSIX path with no traversal, drive letters, ADS (:), or control characters. */
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
  return { ok: true };
}

/** Gate 2: unconditional deny list (repo metadata, scripts, verified snapshots, binaries, crash dumps). */
function gateUnconditionalDeny(relPosix: string): GateVerdict {
  for (const p of ForbiddenPrefixes) {
    if (relPosix.startsWith(p)) {
      return { ok: false, reason: `forbidden prefix ${p}` };
    }
  }
  if (ForbiddenSegment.test(relPosix)) {
    return { ok: false, reason: "forbidden build/dependency segment" };
  }
  if (VerifiedSnapshotPattern.test(relPosix)) {
    return { ok: false, reason: "refusing to overwrite *.verified.* snapshot" };
  }
  if (ForbiddenExtension.test(relPosix)) {
    return { ok: false, reason: "forbidden file extension" };
  }
  return { ok: true };
}

/** Gate 3: strict allowlist (plugin failure artifact rules + knownLocalDirs, or explicit collectPaths). */
function gateAllowlist(
  relPosix: string,
  allowFailureArtifacts: boolean,
  knownLocalDirs: ReadonlySet<string>,
  collectPaths: readonly CollectPathSpec[],
  failureArtifacts: readonly RemoteFailureArtifactRule[],
): GateVerdict {
  if (allowFailureArtifacts) {
    for (const rule of failureArtifacts) {
      if (rule.allowPattern.test(relPosix)) {
        if (rule.requireKnownLocalDir) {
          const dir = path.posix.dirname(relPosix);
          if (!knownLocalDirs.has(dir)) {
            return { ok: false, reason: `parent directory ${dir} does not exist in local manifest` };
          }
        }
        return { ok: true };
      }
    }
  }
  if (/\.cs$/i.test(relPosix)) {
    return { ok: false, reason: "source file write not allowed" };
  }
  for (const spec of collectPaths) {
    const normSpec = path.posix.normalize(spec.path).replace(/\/+$/, "");
    if (spec.kind === "file" && relPosix === normSpec && SafeCollectExtension.test(relPosix)) {
      return { ok: true };
    }
    if (spec.kind === "dir" && relPosix.startsWith(`${normSpec}/`) && SafeCollectExtension.test(relPosix)) {
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

/** Gate 5: symlink / junction / non-regular-file traversal guard along every path segment. */
function gateSymlinkGuard(frame: ArtifactFrame, ctx: GateContext): GateVerdict {
  const segments = frame.relPosix.split("/");
  let cur = ctx.realWorktree;
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
  gateQuotas,
  (frame) => gatePathHygiene(frame.relPosix),
  (frame) => gateUnconditionalDeny(frame.relPosix),
  (frame, ctx) =>
    gateAllowlist(
      frame.relPosix,
      ctx.allowFailureArtifacts,
      ctx.knownLocalDirs,
      ctx.collectPaths,
      ctx.failureArtifacts,
    ),
  gateSymlinkGuard,
];

/** Checks whether a relative POSIX path passes Gates 1-3. */
export function isAllowedArtifactPath(
  relPosix: string,
  allowFailureArtifacts: boolean,
  knownLocalDirs: ReadonlySet<string>,
  collectPaths: readonly CollectPathSpec[],
  failureArtifacts: readonly RemoteFailureArtifactRule[] = [],
): GateVerdict {
  const g1 = gatePathHygiene(relPosix);
  if (!g1.ok) {
    return g1;
  }
  const g2 = gateUnconditionalDeny(relPosix);
  if (!g2.ok) {
    return g2;
  }
  return gateAllowlist(relPosix, allowFailureArtifacts, knownLocalDirs, collectPaths, failureArtifacts);
}

export interface ArtifactWriteOptions {
  worktree: string;
  allowReceived: boolean;
  knownLocalDirs: ReadonlySet<string>;
  collectPaths: readonly CollectPathSpec[];
  failureArtifacts?: readonly RemoteFailureArtifactRule[];
  maxFiles?: number;
  maxSingleBytes?: number;
  maxTotalBytes?: number;
  warn?: (message: string) => void;
}

export interface ArtifactWriteResult {
  writtenFiles: string[];
  rejectedFiles: { path: string; reason: string }[];
  totalBytes: number;
}

/** Runs decoded frames through the 6-gate chain and writes only approved files. */
export function writeValidatedFrames(
  frames: readonly ArtifactFrame[],
  options: ArtifactWriteOptions,
): ArtifactWriteResult {
  const maxSingleBytes = options.maxSingleBytes ?? MaxSingleArtifactBytes;
  const maxTotalBytes = options.maxTotalBytes ?? MaxTotalArtifactBytes;
  const warn = options.warn ?? (() => {});
  const writtenFiles: string[] = [];
  const rejectedFiles: { path: string; reason: string }[] = [];
  let totalBytes = 0;

  if (frames.length === 0) {
    return { writtenFiles, rejectedFiles, totalBytes };
  }

  const realWorktree = fs.realpathSync.native(options.worktree);
  for (const frame of frames) {
    const ctx: GateContext = {
      realWorktree,
      allowFailureArtifacts: options.allowReceived,
      knownLocalDirs: options.knownLocalDirs,
      collectPaths: options.collectPaths,
      failureArtifacts: options.failureArtifacts ?? [],
      maxSingleBytes,
      maxTotalBytes,
      currentTotalBytes: totalBytes,
    };

    let rejected: string | null = null;
    for (const gate of ArtifactGates) {
      const verdict = gate(frame, ctx);
      if (!verdict.ok) {
        rejected = verdict.reason;
        break;
      }
    }
    if (rejected !== null) {
      rejectedFiles.push({ path: frame.relPosix, reason: rejected });
      warn(`rejected remote artifact ${frame.relPosix}: ${rejected}`);
      continue;
    }

    const targetPath = path.join(realWorktree, ...frame.relPosix.split("/"));
    const parentDir = path.dirname(targetPath);
    fs.mkdirSync(parentDir, { recursive: true });
    const realParent = fs.realpathSync.native(parentDir);
    if (!isInsideRoot(realWorktree, realParent)) {
      rejectedFiles.push({ path: frame.relPosix, reason: "resolved parent escapes worktree" });
      warn(`rejected remote artifact ${frame.relPosix}: parent escapes worktree`);
      continue;
    }

    // Gate 6: plain file write (never tar, never chmod +x)
    fs.writeFileSync(targetPath, frame.data);
    writtenFiles.push(frame.relPosix);
    totalBytes += frame.data.length;
  }

  return { writtenFiles, rejectedFiles, totalBytes };
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
    warn(`remote artifact stream rejected (${err.path}): ${err.reason}`);
  }
  const result = writeValidatedFrames(parsed.frames, options);
  result.rejectedFiles.unshift(...parsed.errors);
  return result;
}

/** Builds the remote bash script that streams matching failure artifacts, crash summaries, and collectPaths. */
export function buildArtifactPullScript(
  workspace: string,
  allowFailureArtifacts: boolean,
  collectPaths: readonly CollectPathSpec[],
  remoteRules: ResolvedRemoteRules,
): string | null {
  const collectLines = collectPaths.map((spec) =>
    spec.kind === "file"
      ? `if [ -f ${bashQuote(spec.path)} ] && [ ! -L ${bashQuote(spec.path)} ]; then printf '%s\\0' ${bashQuote(spec.path)}; fi`
      : `if [ -d ${bashQuote(spec.path)} ] && [ ! -L ${bashQuote(spec.path)} ]; then find ${bashQuote(spec.path)} -type f ! -type l ! -name '*.dmp' -print0 2>/dev/null || true; fi`,
  );
  const failureFindLines: string[] = [];
  if (allowFailureArtifacts) {
    for (const rule of [...remoteRules.failureArtifacts, ...remoteRules.crashSummaries]) {
      if (rule.searchDirs.length > 0) {
        const dirs = rule.searchDirs.map((d) => bashQuote(d)).join(" ");
        failureFindLines.push(
          `find ${dirs} -type f ! -type l -name ${bashQuote(rule.findName)} -print0 2>/dev/null || true`,
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
    '  [ -f "$f" ] && [ ! -L "$f" ] || return 0',
    '  local sz=$(stat -c %s "$f" 2>/dev/null || echo -1)',
    '  [ "$sz" -ge 0 ] || return 0',
    '  printf "%s\\0%s\\n" "$f" "$sz"',
    '  cat "$f"',
    "}",
    "{",
    ...failureFindLines.map((l) => `  ${l}`),
    ...collectLines.map((l) => `  ${l}`),
    "} | while IFS= read -r -d '' f; do emit_file \"$f\"; done",
  ].join("\n");
}

export interface PullArtifactsResult {
  written: number;
  rejected: number;
  bytes: number;
  crashedTests: string[];
}

/** Pulls and validates remote failure artifacts + in-memory crash summaries in a single frame pass. */
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
  ): Promise<PullArtifactsResult> {
    const script = buildArtifactPullScript(
      this.transport.spec.workspace,
      allowFailureArtifacts,
      collectPaths,
      this.remoteRules,
    );
    if (script === null) {
      return { written: 0, rejected: 0, bytes: 0, crashedTests: [] };
    }
    const payload = await this.transport.execBinary(script, MaxTotalArtifactBytes + 1024 * 1024);
    if (payload === null || payload.length === 0) {
      return { written: 0, rejected: 0, bytes: 0, crashedTests: [] };
    }

    const parsed = parseArtifactFrames(payload);
    const crashedTests: string[] = [];
    const artifactFrames: ArtifactFrame[] = [];
    for (const frame of parsed.frames) {
      const crashRule = allowFailureArtifacts
        ? this.remoteRules.crashSummaries.find((rule) => rule.pathPattern.test(frame.relPosix))
        : undefined;
      if (crashRule !== undefined) {
        crashedTests.push(...crashRule.parse(frame.data.toString("utf8")));
      } else {
        artifactFrames.push(frame);
      }
    }

    const writeResult = writeValidatedFrames(artifactFrames, {
      worktree,
      allowReceived: allowFailureArtifacts,
      knownLocalDirs,
      collectPaths,
      failureArtifacts: this.remoteRules.failureArtifacts,
      warn: prefix,
    });
    writeResult.rejectedFiles.unshift(...parsed.errors);

    return {
      written: writeResult.writtenFiles.length,
      rejected: writeResult.rejectedFiles.length,
      bytes: writeResult.totalBytes,
      crashedTests: [...new Set(crashedTests)],
    };
  }
}
