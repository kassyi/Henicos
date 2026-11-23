// Zero-trust boundary for all data returned from a remote worker:
//   1. RemoteStreamSanitizer: stateful UTF-8 + ANSI filter allowing printable text, CR/LF/TAB, and SGR color codes
//      (\x1b[...m) while stripping OSC (clipboard/title), DCS/APC/PM/SOS, cursor-movement CSI, BEL, BS, and C1 controls.
//   2. validateAndWriteArtifacts: zero-tar 6-gate validated receiver for length-framed files (<path>\0<bytes>\n<payload>),
//      enforcing strict path/extension allowlists, symlink/junction realpath guards, and hard size/count quotas.

import * as fs from "node:fs";
import * as path from "node:path";
import { StringDecoder } from "node:string_decoder";
import type { RemoteFailureArtifactRule } from "./plugin-api.ts";

export interface CollectPathSpec {
  /** Worktree-relative normalized POSIX path (no leading/trailing slash). */
  path: string;
  /** "file" for exact redirection target (`> tmp/out.log`); "dir" for `--results-directory <dir>`. */
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
            // Single-character ESC sequence (e.g. ESC c reset, ESC 7/8 cursor save): drop
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
            // Malformed or aborted CSI
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

const ForbiddenPrefixes = [".git/", "scripts/", ".agents/", ".claude/", "node_modules/"];
const ForbiddenSegment = /(?:^|\/)(?:bin|obj|node_modules)\//;
const VerifiedSnapshotPattern = /\.verified\.[^/]+$/i;
const ForbiddenExtension =
  /\.(?:csproj|fsproj|vbproj|props|targets|sln|slnx|slnf|js|mjs|cjs|ts|mts|cts|ps1|psm1|sh|bash|cmd|bat|exe|dll|so|dylib|dmp|pdb|reg|vbs|wsf|hta)$/i;
const SafeCollectExtension = /\.(?:trx|xml|json|log|txt|md|html|csv|tsv)$/i;

/** Normalizes a path for case-insensitive containment checks on Windows. */
function normKey(p: string): string {
  const resolved = path.resolve(p);
  return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}

function isInsideRoot(root: string, target: string): boolean {
  const rel = path.relative(normKey(root), normKey(target));
  return rel === "" || (!rel.startsWith("..") && !path.isAbsolute(rel));
}

/**
 * Checks whether a relative POSIX path from a remote worker passes Gates 1-3 (path hygiene, forbidden area deny, and
 * strict allowlist against plugin `failureArtifacts` + `knownLocalDirs` or explicit `collectPaths`).
 */
export function isAllowedArtifactPath(
  relPosix: string,
  allowFailureArtifacts: boolean,
  knownLocalDirs: ReadonlySet<string>,
  collectPaths: readonly CollectPathSpec[],
  failureArtifacts: readonly RemoteFailureArtifactRule[] = [],
): { ok: true } | { ok: false; reason: string } {
  // Gate 1: normalized relative POSIX path with no traversal, drive letters, ADS (:), or control characters
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

  // Gate 2: unconditional deny list (repo metadata, scripts, verified snapshots, binaries, crash dumps)
  for (const prefix of ForbiddenPrefixes) {
    if (relPosix.startsWith(prefix)) {
      return { ok: false, reason: `forbidden prefix ${prefix}` };
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

  // Gate 3: strict allowlist (plugin-supplied failure artifact rules, or explicit collectPaths)
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

  // Regular .cs files are never allowed outside plugin-approved failure artifacts
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

/**
 * Parses length-framed records (`<relPosixPath>\0<decimalByteLen>\n<rawBytes>`) from `payload` and writes only those
 * passing all 6 security gates (normalization, denylist, allowlist, size/count quotas, symlink/junction realpath
 * traversal guard, plain `fs.writeFileSync`). Never invokes `tar`.
 */
export function validateAndWriteArtifacts(payload: Buffer, options: ArtifactWriteOptions): ArtifactWriteResult {
  const maxFiles = options.maxFiles ?? MaxArtifactFiles;
  const maxSingleBytes = options.maxSingleBytes ?? MaxSingleArtifactBytes;
  const maxTotalBytes = options.maxTotalBytes ?? MaxTotalArtifactBytes;
  const warn = options.warn ?? (() => {});

  const writtenFiles: string[] = [];
  const rejectedFiles: { path: string; reason: string }[] = [];
  let totalBytes = 0;

  if (payload.length === 0) {
    return { writtenFiles, rejectedFiles, totalBytes };
  }
  if (payload.length > maxTotalBytes + maxFiles * 512) {
    rejectedFiles.push({ path: "<stream>", reason: `payload exceeds total quota (${maxTotalBytes} bytes)` });
    warn(`remote artifact stream rejected: payload exceeds ${maxTotalBytes} bytes`);
    return { writtenFiles, rejectedFiles, totalBytes };
  }

  const realWorktree = fs.realpathSync.native(options.worktree);
  let offset = 0;
  let seenFiles = 0;

  while (offset < payload.length) {
    if (seenFiles >= maxFiles) {
      rejectedFiles.push({ path: "<stream>", reason: `exceeded max file count (${maxFiles})` });
      warn(`remote artifact stream truncated: exceeded ${maxFiles} files`);
      break;
    }
    const nulIndex = payload.indexOf(0x00, offset);
    if (nulIndex < 0) {
      rejectedFiles.push({ path: "<stream>", reason: "malformed frame: missing path terminator" });
      break;
    }
    const relPosix = payload.subarray(offset, nulIndex).toString("utf8");
    const nlIndex = payload.indexOf(0x0a, nulIndex + 1);
    if (nlIndex < 0) {
      rejectedFiles.push({ path: relPosix || "<stream>", reason: "malformed frame: missing size terminator" });
      break;
    }
    const sizeText = payload.subarray(nulIndex + 1, nlIndex).toString("ascii");
    if (!/^\d{1,12}$/.test(sizeText)) {
      rejectedFiles.push({ path: relPosix, reason: `malformed frame size "${sizeText}"` });
      break;
    }
    const byteLen = Number(sizeText);
    const dataStart = nlIndex + 1;
    const dataEnd = dataStart + byteLen;
    if (!Number.isSafeInteger(byteLen) || byteLen < 0 || dataEnd > payload.length) {
      rejectedFiles.push({ path: relPosix, reason: "truncated frame payload" });
      break;
    }
    offset = dataEnd;
    seenFiles++;

    // Gate 4: size quotas
    if (byteLen > maxSingleBytes) {
      rejectedFiles.push({ path: relPosix, reason: `exceeds single-file quota (${maxSingleBytes} bytes)` });
      warn(`rejected remote artifact ${relPosix}: exceeds ${maxSingleBytes} bytes`);
      continue;
    }
    if (totalBytes + byteLen > maxTotalBytes) {
      rejectedFiles.push({ path: relPosix, reason: `exceeds total quota (${maxTotalBytes} bytes)` });
      warn(`rejected remote artifact ${relPosix}: exceeds total ${maxTotalBytes} bytes`);
      continue;
    }

    // Gates 1-3: path hygiene, forbidden list, allowlist
    const verdict = isAllowedArtifactPath(
      relPosix,
      options.allowReceived,
      options.knownLocalDirs,
      options.collectPaths,
      options.failureArtifacts ?? [],
    );
    if (!verdict.ok) {
      rejectedFiles.push({ path: relPosix, reason: verdict.reason });
      warn(`rejected remote artifact ${relPosix}: ${verdict.reason}`);
      continue;
    }

    // Gate 5: symlink / junction / realpath escape guard along every segment
    const segments = relPosix.split("/");
    let cur = realWorktree;
    let symlinkProblem: string | null = null;
    for (let i = 0; i < segments.length; i++) {
      cur = path.join(cur, segments[i] as string);
      let st: fs.Stats | null = null;
      try {
        st = fs.lstatSync(cur);
      } catch (err) {
        if (err && typeof err === "object" && "code" in err && (err as { code?: string }).code === "ENOENT") {
          st = null;
        } else {
          symlinkProblem = `lstat failed on ${relPosix}`;
          break;
        }
      }
      if (st !== null) {
        if (st.isSymbolicLink()) {
          symlinkProblem = `symlink/junction detected along path ${relPosix}`;
          break;
        }
        const isLast = i === segments.length - 1;
        if (isLast && !st.isFile()) {
          symlinkProblem = `existing target is not a regular file: ${relPosix}`;
          break;
        }
        if (!isLast && !st.isDirectory()) {
          symlinkProblem = `existing ancestor is not a directory: ${relPosix}`;
          break;
        }
      }
    }
    if (symlinkProblem !== null) {
      rejectedFiles.push({ path: relPosix, reason: symlinkProblem });
      warn(`rejected remote artifact ${relPosix}: ${symlinkProblem}`);
      continue;
    }

    const targetPath = path.join(realWorktree, ...segments);
    const parentDir = path.dirname(targetPath);
    fs.mkdirSync(parentDir, { recursive: true });
    const realParent = fs.realpathSync.native(parentDir);
    if (!isInsideRoot(realWorktree, realParent)) {
      rejectedFiles.push({ path: relPosix, reason: "resolved parent escapes worktree" });
      warn(`rejected remote artifact ${relPosix}: parent escapes worktree`);
      continue;
    }

    // Gate 6: plain file write (never tar, never chmod +x)
    fs.writeFileSync(targetPath, payload.subarray(dataStart, dataEnd));
    writtenFiles.push(relPosix);
    totalBytes += byteLen;
  }

  return { writtenFiles, rejectedFiles, totalBytes };
}
