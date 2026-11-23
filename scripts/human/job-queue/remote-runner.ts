// Executes a translated CPU job on a configured remote Linux worker over hardened SSH:
//   1. Fast delta sync with full SHA-256 manifest (.job-queue-manifest.sha256) + exact file-set parity verification
//      before every run (keeps bin/linux & obj/linux warm while auto-healing any mutated or rogue source file)
//   2. Atomic execution under remote flock with hard wall-clock timeout, stdin-EOF lifeline watchdog (FD 3), and
//      O_RDWR FIFO + 128-bit EOF sentinel (`sed -u -n`) so 100% of test stack traces flush losslessly while warm
//      VBCSCompiler / MSBuild daemons never hold sshd open
//   3. Zero-trust output boundary: live stdout/stderr pass through RemoteStreamSanitizer, native crashes print the
//      in-progress test from VSTest `--blame` Sequence_*.xml, and *.received.* / explicit collectPaths are retrieved
//      via length-framed streaming through validateAndWriteArtifacts (never `tar -xf` on Windows).

import { spawn, type ChildProcess } from "node:child_process";
import * as crypto from "node:crypto";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { buildSshArgs, type RemoteSpec } from "./remotes.ts";
import { bashQuote, type RemoteCommandPlan } from "./remote-command.ts";
import {
  MaxTotalArtifactBytes,
  RemoteStreamSanitizer,
  parseSequenceXmlInProgress,
  validateAndWriteArtifacts,
  type CollectPathSpec,
} from "./remote-output.ts";
import {
  collectLocalManifest,
  diffManifests,
  formatSha256Manifest,
  loadRemoteManifestCache,
  manifestParentDirs,
  parseRemoteSha256Output,
  saveRemoteManifestCache,
} from "./remote-sync.ts";
import type { ShellResult } from "./shell.ts";
import { prefix, resolveStateDirectory } from "./system.ts";

export const RemoteManifestDigestFile = ".job-queue-manifest.digest";
export const RemoteManifestSha256File = ".job-queue-manifest.sha256";
export const RemoteLockFile = ".job-queue-slot.lock";

/** Prune expression matching `isExcludedSyncPath` in remote-sync.ts (keeps bin/obj/StackLinqEmbedded warm). */
const RemoteFindPruneExpr =
  `\\( -path './.git' -o -path './tmp' -o -path './TestResults' -o -path './BenchmarkDotNet.Artifacts' ` +
  `-o -path './scratch' -o -path './.vs' -o -path './.claude/worktrees' ` +
  `-o -path './src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded' ` +
  `-o -path './${RemoteManifestDigestFile}' -o -path './${RemoteManifestSha256File}' -o -path './${RemoteLockFile}' ` +
  `-o -name 'bin' -o -name 'obj' -o -name 'node_modules' \\) -prune -o ` +
  `\\( -type f ! -name '*.received.*' ! -name '*.log' ! -name '*.user' -print0 \\)`;

function tarBinary(): string {
  return process.platform === "win32" ? "tar.exe" : "tar";
}

/** Expands leading `~/` safely inside bash without quoting away tilde expansion. */
export function bashWorkspaceExpr(workspace: string): string {
  if (workspace === "~") {
    return `"$HOME"`;
  }
  if (workspace.startsWith("~/")) {
    return `"$HOME"/${bashQuote(workspace.slice(2))}`;
  }
  return bashQuote(workspace);
}

/** Runs a short control SSH command and captures stdout/stderr. */
function runSshCapture(remote: RemoteSpec, script: string): Promise<{ code: number; stdout: string; stderr: string }> {
  return new Promise((resolve) => {
    const child = spawn("ssh", buildSshArgs(remote, "bash -s"), {
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });
    let stdout = "";
    let stderr = "";
    child.stdout?.on("data", (d: Buffer) => (stdout += d.toString("utf8")));
    child.stderr?.on("data", (d: Buffer) => (stderr += d.toString("utf8")));
    child.on("error", (err) => resolve({ code: 255, stdout, stderr: err.message }));
    child.stdin?.on("error", () => {});
    child.stdin?.end(script.replace(/\r\n/g, "\n"));
    child.on("close", (code) => resolve({ code: code ?? 1, stdout, stderr }));
  });
}

/** Fetches the remote workspace's current file->sha256 map when local cache is missing or out of sync. */
async function fetchRemoteManifest(remote: RemoteSpec): Promise<Record<string, string> | null> {
  const ws = bashWorkspaceExpr(remote.workspace);
  const script = [
    "set -euo pipefail",
    `WS=${ws}`,
    `mkdir -p "$WS"`,
    `cd "$WS"`,
    `find . ${RemoteFindPruneExpr} | xargs -0 -r sha256sum`,
  ].join("\n");
  const res = await runSshCapture(remote, script);
  if (res.code !== 0) {
    return null;
  }
  return parseRemoteSha256Output(res.stdout);
}

/**
 * Streams `.job-queue-manifest.sha256` and (when non-empty) changed files via `tar` over SSH stdin, and removes
 * deleted files under the remote workspace flock.
 */
async function applyDeltaSync(
  remote: RemoteSpec,
  worktree: string,
  toUpload: string[],
  toDelete: string[],
  localDigest: string,
  sha256ManifestText: string,
  expectedRemoteDigest?: string,
): Promise<{ ok: boolean; stale?: boolean; error?: string }> {
  const ws = bashWorkspaceExpr(remote.workspace);
  const deleteBuf = toDelete.length === 0 ? Buffer.alloc(0) : Buffer.from(`${toDelete.join("\0")}\0`, "utf8");
  const manifestBuf = Buffer.from(sha256ManifestText, "utf8");
  const guard =
    expectedRemoteDigest !== undefined
      ? `if [ "$(cat ${RemoteManifestDigestFile} 2>/dev/null || true)" != ${bashQuote(expectedRemoteDigest)} ]; then exit 254; fi`
      : "";
  const remoteCmd = [
    "bash -c",
    bashQuote(
      [
        "set -euo pipefail",
        `WS=${ws}`,
        `mkdir -p "$WS"`,
        `exec 9>"$WS/${RemoteLockFile}"`,
        "flock 9",
        `cd "$WS"`,
        guard,
        "IFS= read -r DEL_BYTES",
        'if [ "$DEL_BYTES" -gt 0 ]; then dd iflag=fullblock bs="$DEL_BYTES" count=1 2>/dev/null | xargs -0 -r rm -f --; fi',
        "IFS= read -r MANIFEST_BYTES",
        `if [ "$MANIFEST_BYTES" -gt 0 ]; then dd iflag=fullblock bs="$MANIFEST_BYTES" count=1 2>/dev/null > ${RemoteManifestSha256File}.tmp; else : > ${RemoteManifestSha256File}.tmp; fi`,
        toUpload.length > 0 ? "tar -xf -" : "",
        `mv -f ${RemoteManifestSha256File}.tmp ${RemoteManifestSha256File}`,
        `printf '%s\\n' ${bashQuote(localDigest)} > ${RemoteManifestDigestFile}`,
      ]
        .filter(Boolean)
        .join("\n"),
    ),
  ].join(" ");

  if (toUpload.length === 0) {
    return await new Promise((resolve) => {
      const ssh = spawn("ssh", buildSshArgs(remote, remoteCmd), {
        stdio: ["pipe", "ignore", "pipe"],
        windowsHide: true,
      });
      let errText = "";
      ssh.stderr?.on("data", (d: Buffer) => (errText += d.toString("utf8")));
      ssh.on("error", (err) => resolve({ ok: false, error: err.message }));
      ssh.stdin?.on("error", () => {});
      ssh.stdin?.write(`${deleteBuf.length}\n`);
      if (deleteBuf.length > 0) {
        ssh.stdin?.write(deleteBuf);
      }
      ssh.stdin?.write(`${manifestBuf.length}\n`);
      ssh.stdin?.end(manifestBuf);
      ssh.on("close", (code) => {
        if (code === 254) {
          resolve({ ok: false, stale: true });
        } else {
          resolve(code === 0 ? { ok: true } : { ok: false, error: errText.trim() || `ssh exit ${code}` });
        }
      });
    });
  }

  const listFile = path.join(os.tmpdir(), `job-queue-sync-${process.pid}-${Date.now()}.txt`);
  fs.writeFileSync(listFile, `${toUpload.join("\n")}\n`, "utf8");
  try {
    return await new Promise((resolve) => {
      const tar = spawn(tarBinary(), ["-cf", "-", "-T", listFile], {
        cwd: worktree,
        stdio: ["ignore", "pipe", "pipe"],
        windowsHide: true,
      });
      const ssh = spawn("ssh", buildSshArgs(remote, remoteCmd), {
        stdio: ["pipe", "ignore", "pipe"],
        windowsHide: true,
      });
      let errText = "";
      tar.stderr?.on("data", (d: Buffer) => (errText += d.toString("utf8")));
      ssh.stderr?.on("data", (d: Buffer) => (errText += d.toString("utf8")));
      const sshIn = ssh.stdin as NodeJS.WritableStream;
      sshIn.on("error", () => {});
      sshIn.write(`${deleteBuf.length}\n`);
      if (deleteBuf.length > 0) {
        sshIn.write(deleteBuf);
      }
      sshIn.write(`${manifestBuf.length}\n`);
      sshIn.write(manifestBuf);
      tar.stdout?.pipe(sshIn);

      let tarCode: number | null = null;
      let sshCode: number | null = null;
      let settled = false;
      const finish = (result: { ok: boolean; stale?: boolean; error?: string }): void => {
        if (!settled) {
          settled = true;
          resolve(result);
        }
      };
      const done = (): void => {
        if (tarCode !== null && sshCode !== null) {
          if (sshCode === 254) {
            finish({ ok: false, stale: true });
          } else {
            finish(
              tarCode === 0 && sshCode === 0
                ? { ok: true }
                : { ok: false, error: errText.trim() || `tar=${tarCode} ssh=${sshCode}` },
            );
          }
        }
      };
      tar.on("error", (err) => {
        errText += err.message;
        tarCode = 1;
        ssh.stdin?.destroy();
        ssh.kill();
        done();
      });
      ssh.on("error", (err) => {
        errText += err.message;
        sshCode = 255;
        tar.stdout?.destroy();
        tar.stderr?.destroy();
        tar.kill();
        done();
      });
      tar.on("close", (code) => {
        tarCode = code ?? 1;
        if (tarCode !== 0 && sshCode === null) {
          ssh.stdin?.destroy();
          ssh.kill();
        }
        done();
      });
      ssh.on("close", (code) => {
        sshCode = code ?? 1;
        if (tarCode === null) {
          tar.stdout?.destroy();
          tar.stderr?.destroy();
          tar.kill();
        }
        if (sshCode === 254) {
          finish({ ok: false, stale: true });
          return;
        }
        done();
      });
    });
  } finally {
    fs.rmSync(listFile, { force: true });
  }
}

/**
 * Pulls back failed Verify `*.received.*` files and explicit `collectPaths` via length-framed regular-file streaming
 * and writes them strictly through `validateAndWriteArtifacts` (no `tar -xf` on Windows). Also reports any aborted
 * test names recorded by VSTest `--blame` `Sequence_*.xml` when `allowReceived` is true (non-zero exit).
 */
async function pullValidatedArtifacts(
  remote: RemoteSpec,
  worktree: string,
  allowReceived: boolean,
  knownLocalDirs: ReadonlySet<string>,
  collectPaths: readonly CollectPathSpec[],
): Promise<{ written: number; rejected: number; bytes: number; crashedTests: string[] }> {
  const ws = bashWorkspaceExpr(remote.workspace);
  const collectLines = collectPaths.map((spec) =>
    spec.kind === "file"
      ? `if [ -f ${bashQuote(spec.path)} ] && [ ! -L ${bashQuote(spec.path)} ]; then printf '%s\\0' ${bashQuote(spec.path)}; fi`
      : `if [ -d ${bashQuote(spec.path)} ] && [ ! -L ${bashQuote(spec.path)} ]; then find ${bashQuote(spec.path)} -type f ! -type l ! -name '*.dmp' -print0 2>/dev/null || true; fi`,
  );

  // Also collect Sequence_*.xml on failure to report native crash / OOM test attribution
  const script = [
    "set -euo pipefail",
    `WS=${ws}`,
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
    allowReceived ? "  find tests src -type f ! -type l -name '*.received.*' -print0 2>/dev/null || true" : "",
    allowReceived ? "  find TestResults -type f ! -type l -name 'Sequence_*.xml' -print0 2>/dev/null || true" : "",
    ...collectLines.map((l) => `  ${l}`),
    "} | while IFS= read -r -d '' f; do emit_file \"$f\"; done",
  ]
    .filter(Boolean)
    .join("\n");

  const payload = await new Promise<Buffer | null>((resolve) => {
    const ssh = spawn("ssh", buildSshArgs(remote, "bash -s"), {
      stdio: ["pipe", "pipe", "ignore"],
      windowsHide: true,
    });
    const chunks: Buffer[] = [];
    let total = 0;
    const hardLimit = MaxTotalArtifactBytes + 1024 * 1024;
    let aborted = false;
    ssh.stdout?.on("data", (d: Buffer) => {
      total += d.length;
      if (total > hardLimit) {
        aborted = true;
        ssh.kill();
        return;
      }
      chunks.push(d);
    });
    ssh.on("error", () => resolve(null));
    ssh.stdin?.on("error", () => {});
    ssh.stdin?.end(script);
    ssh.on("close", (code) => {
      if (aborted || code !== 0 || chunks.length === 0) {
        resolve(null);
        return;
      }
      resolve(Buffer.concat(chunks));
    });
  });

  if (payload === null || payload.length === 0) {
    return { written: 0, rejected: 0, bytes: 0, crashedTests: [] };
  }

  // Extract crashed test names from any Sequence_*.xml frames in memory (never written to worktree unless requested)
  const crashedTests: string[] = [];
  const filteredFrames: Buffer[] = [];
  let offset = 0;
  while (offset < payload.length) {
    const nul = payload.indexOf(0x00, offset);
    if (nul < 0) {
      break;
    }
    const rel = payload.subarray(offset, nul).toString("utf8");
    const nl = payload.indexOf(0x0a, nul + 1);
    if (nl < 0) {
      break;
    }
    const sizeText = payload.subarray(nul + 1, nl).toString("ascii");
    if (!/^\d{1,12}$/.test(sizeText)) {
      break;
    }
    const byteLen = Number(sizeText);
    const end = nl + 1 + byteLen;
    if (end > payload.length) {
      break;
    }
    if (/^TestResults\/.*Sequence_[^/]+\.xml$/i.test(rel)) {
      const xml = payload.subarray(nl + 1, end).toString("utf8");
      crashedTests.push(...parseSequenceXmlInProgress(xml));
    } else {
      filteredFrames.push(payload.subarray(offset, end));
    }
    offset = end;
  }

  const writeResult = validateAndWriteArtifacts(Buffer.concat(filteredFrames), {
    worktree,
    allowReceived,
    knownLocalDirs,
    collectPaths,
    warn: prefix,
  });

  return {
    written: writeResult.writtenFiles.length,
    rejected: writeResult.rejectedFiles.length,
    bytes: writeResult.totalBytes,
    crashedTests: [...new Set(crashedTests)],
  };
}

export interface RemoteRunResult extends ShellResult {
  /** True when the remote SSH transport itself failed before executing the command (caller may fall back to local). */
  transportFailed?: boolean;
}

/**
 * Syncs `plan.worktree` to `remote`, verifies source integrity (exact file-set + `sha256sum --quiet -c`) under
 * remote flock, runs `plan.bashCommand` with lossless EOF-sentinel FIFO drain and sanitized live stdio, and pulls back
 * validated `*.received.*` / explicit output artifacts without `tar -xf`.
 */
export async function executeRemotePlan(
  remote: RemoteSpec,
  plan: RemoteCommandPlan,
  onChild: (child: ChildProcess) => void,
  stateDir = resolveStateDirectory(),
): Promise<RemoteRunResult> {
  const syncStart = performance.now();
  const statCachePath = path.join(stateDir, "offload-stat-cache.json");
  const remoteCachePath = path.join(stateDir, `remote-manifest-${remote.name}.json`);

  const localManifest = collectLocalManifest(plan.worktree, statCachePath);
  const sha256ManifestText = formatSha256Manifest(localManifest);
  const cached = loadRemoteManifestCache(remoteCachePath);

  const syncWithFetch = async (): Promise<{ uploaded: number; deleted: number; digest: string } | null> => {
    const fetched = await fetchRemoteManifest(remote);
    if (fetched === null) {
      return null;
    }
    const diff = diffManifests(localManifest, fetched);
    const applied = await applyDeltaSync(
      remote,
      plan.worktree,
      diff.toUpload,
      diff.toDelete,
      diff.localDigest,
      sha256ManifestText,
    );
    if (!applied.ok) {
      prefix(`remote sync to ${remote.name} (${remote.host}) failed: ${applied.error ?? "unknown"}`);
      return null;
    }
    saveRemoteManifestCache(remoteCachePath, localManifest);
    return { uploaded: diff.toUpload.length, deleted: diff.toDelete.length, digest: diff.localDigest };
  };

  let uploaded = 0;
  let deleted = 0;
  let expectedDigest = "";

  if (cached === null) {
    const res = await syncWithFetch();
    if (res === null) {
      return { code: 255, signaled: false, transportFailed: true };
    }
    uploaded = res.uploaded;
    deleted = res.deleted;
    expectedDigest = res.digest;
  } else {
    const diff = diffManifests(localManifest, cached.files);
    expectedDigest = diff.localDigest;
    if (diff.toUpload.length > 0 || diff.toDelete.length > 0) {
      const applied = await applyDeltaSync(
        remote,
        plan.worktree,
        diff.toUpload,
        diff.toDelete,
        diff.localDigest,
        sha256ManifestText,
        cached.digest,
      );
      if (applied.stale) {
        const res = await syncWithFetch();
        if (res === null) {
          return { code: 255, signaled: false, transportFailed: true };
        }
        uploaded = res.uploaded;
        deleted = res.deleted;
      } else if (!applied.ok) {
        prefix(`remote sync to ${remote.name} (${remote.host}) failed: ${applied.error ?? "unknown"}`);
        return { code: 255, signaled: false, transportFailed: true };
      } else {
        uploaded = diff.toUpload.length;
        deleted = diff.toDelete.length;
        saveRemoteManifestCache(remoteCachePath, localManifest);
      }
    }
  }
  const syncSec = ((performance.now() - syncStart) / 1000).toFixed(2);
  prefix(`remote ${remote.name} (${remote.host}): synced ${uploaded} changed, ${deleted} deleted in ${syncSec}s`);

  const runOnce = (verifyIntegrity: boolean): Promise<ShellResult> => {
    const ws = bashWorkspaceExpr(remote.workspace);
    const eofToken = `__JQ_EOF_${crypto.randomBytes(16).toString("hex")}__`;
    const timeoutSec = Math.max(60, Math.round(remote.maxJobMinutes * 60));
    const envExports = Object.entries(plan.env)
      .map(([k, v]) => `export ${k}=${bashQuote(v)}`)
      .join("\n");
    const cdSubdir = plan.remoteCwdRel !== "" ? `cd ${bashQuote(plan.remoteCwdRel)}` : "";
    const mkdirDirs = [
      ...new Set(plan.collectPaths.map((c) => (c.kind === "dir" ? c.path : path.posix.dirname(c.path)))),
    ]
      .filter((d) => d !== "" && d !== ".")
      .map((d) => `mkdir -p -- ${bashQuote(d)}`)
      .join("\n");

    // Pre-execution cryptographic source integrity check (exact file set + sha256sum --quiet -c):
    // catches both mutated source files and newly injected untracked files while keeping bin/obj warm (~0.5s).
    const integrityCheck = verifyIntegrity
      ? [
          `if [ "$(cat ${RemoteManifestDigestFile} 2>/dev/null || true)" != ${bashQuote(expectedDigest)} ]; then exit 254; fi`,
          `if [ ! -f ${RemoteManifestSha256File} ]; then exit 254; fi`,
          `EXPECTED_LIST=$(mktemp)`,
          `ACTUAL_LIST=$(mktemp)`,
          `LC_ALL=C awk '{print substr($0, 67)}' ${RemoteManifestSha256File} | LC_ALL=C sort > "$EXPECTED_LIST"`,
          `find . ${RemoteFindPruneExpr} | tr '\\0' '\\n' | LC_ALL=C sort > "$ACTUAL_LIST"`,
          `if ! cmp -s "$EXPECTED_LIST" "$ACTUAL_LIST"; then rm -f "$EXPECTED_LIST" "$ACTUAL_LIST"; exit 254; fi`,
          `rm -f "$EXPECTED_LIST" "$ACTUAL_LIST"`,
          `if ! sha256sum --quiet -c ${RemoteManifestSha256File} >/dev/null 2>&1; then exit 254; fi`,
        ].join("\n")
      : "";

    const execScript = [
      "set -euo pipefail",
      `WS=${ws}`,
      `exec 9>"$WS/${RemoteLockFile}"`,
      "flock 9",
      `cd "$WS"`,
      integrityCheck,
      "find tests src -name '*.received.*' -delete 2>/dev/null || true",
      "find TestResults -name 'Sequence_*.xml' -delete 2>/dev/null || true",
      mkdirDirs,
      cdSubdir,
      'export PATH="/usr/local/bin:$HOME/.dotnet:$HOME/.cargo/bin:$PATH"',
      envExports,
      "set +e",
      // Duplicate SSH stdin onto FD 3 so the lifeline watchdog detects client cancel/disconnect immediately.
      // Hold O_RDWR FDs 7/8 on OUT_FIFO/ERR_FIFO so writer refcount never drops to 0 before we emit the 128-bit EOF
      // sentinel; `sed -u -n` flushes 100% of stdout/stderr live and exits cleanly at the sentinel even when warm
      // VBCSCompiler / MSBuild daemons inherit the FIFO write descriptors.
      "exec 3<&0",
      "OUT_FIFO=$(mktemp -u)",
      "ERR_FIFO=$(mktemp -u)",
      'mkfifo "$OUT_FIFO" "$ERR_FIFO"',
      'exec 7<>"$OUT_FIFO" 8<>"$ERR_FIFO"',
      `sed -u -n "/^${eofToken}\$/q;p" <"$OUT_FIFO" 7<&- 8<&- 3<&- 9>&- &`,
      "PUMP_OUT=$!",
      `sed -u -n "/^${eofToken}\$/q;p" <"$ERR_FIFO" >&2 7<&- 8<&- 3<&- 9>&- &`,
      "PUMP_ERR=$!",
      `setsid timeout --kill-after=10s ${timeoutSec}s bash -c ${bashQuote(plan.bashCommand)} </dev/null >"$OUT_FIFO" 2>"$ERR_FIFO" 3<&- 7<&- 8<&- 9>&- &`,
      "CMD_PID=$!",
      "(",
      "  while IFS= read -r _ <&3; do :; done",
      '  if kill -0 "$CMD_PID" 2>/dev/null; then',
      '    kill -TERM -- -"$CMD_PID" 2>/dev/null || true',
      "    sleep 1",
      '    kill -KILL -- -"$CMD_PID" 2>/dev/null || true',
      "  fi",
      ") >/dev/null 2>&1 7<&- 8<&- 9>&- &",
      "WATCH_PID=$!",
      'wait "$CMD_PID"',
      "RC=$?",
      'kill "$WATCH_PID" 2>/dev/null || true',
      `printf '\\n%s\\n' ${bashQuote(eofToken)} >&7`,
      `printf '\\n%s\\n' ${bashQuote(eofToken)} >&8`,
      'wait "$PUMP_OUT" "$PUMP_ERR" 2>/dev/null || true',
      "exec 7<&- 8<&- 3<&-",
      'rm -f "$OUT_FIFO" "$ERR_FIFO"',
      "exit $RC",
    ]
      .filter(Boolean)
      .join("\n");

    return new Promise<ShellResult>((resolve) => {
      const child = spawn("ssh", buildSshArgs(remote, `bash -c ${bashQuote(execScript)}`), {
        // Keep stdin open as the lifeline pipe; sanitize stdout/stderr through RemoteStreamSanitizer.
        stdio: ["pipe", "pipe", "pipe"],
        windowsHide: true,
        detached: process.platform !== "win32",
      });
      const outSan = new RemoteStreamSanitizer();
      const errSan = new RemoteStreamSanitizer();
      child.stdout?.on("data", (d: Buffer) => {
        const clean = outSan.push(d);
        if (clean !== "") {
          process.stdout.write(clean);
        }
      });
      child.stderr?.on("data", (d: Buffer) => {
        const clean = errSan.push(d);
        if (clean !== "") {
          process.stderr.write(clean);
        }
      });
      child.on("error", (err) => {
        prefix(`could not start ssh to ${remote.host}: ${err.message}`);
        resolve({ code: 255, signaled: false });
      });
      if (child.pid !== undefined) {
        onChild(child);
      }
      child.on("close", (code, signal) => {
        const outTail = outSan.flush();
        if (outTail !== "") {
          process.stdout.write(outTail);
        }
        const errTail = errSan.flush();
        if (errTail !== "") {
          process.stderr.write(errTail);
        }
        const bySignal = signal !== null || (process.platform !== "win32" && code !== null && code > 128 && code < 160);
        resolve({ code: code === null ? 1 : code, signaled: bySignal });
      });
    });
  };

  // Always verify remote source integrity (exact file set + sha256sum -c) before execution, even after a partial
  // delta upload, so any mutation to an unchanged file from a previous test is detected and healed.
  let runResult = await runOnce(true);
  if (runResult.code === 254) {
    prefix(`remote ${remote.name}: workspace integrity mismatch detected; re-syncing clean sources`);
    const res = await syncWithFetch();
    if (res === null) {
      return { code: 255, signaled: false, transportFailed: true };
    }
    prefix(`remote ${remote.name}: repaired ${res.uploaded} changed, ${res.deleted} deleted`);
    runResult = await runOnce(false);
  }
  if (runResult.code === 255 && !runResult.signaled) {
    return { ...runResult, transportFailed: true };
  }

  // Pull back validated Verify *.received.* (on failure), native crash Sequence_*.xml names, and explicit collectPaths
  if (!runResult.signaled && (runResult.code !== 0 || plan.collectPaths.length > 0)) {
    const pulled = await pullValidatedArtifacts(
      remote,
      plan.worktree,
      runResult.code !== 0,
      manifestParentDirs(localManifest),
      plan.collectPaths,
    );
    if (pulled.crashedTests.length > 0) {
      prefix(`remote ${remote.name}: test host crashed/aborted while running: ${pulled.crashedTests.join(", ")}`);
    }
    if (pulled.written > 0) {
      prefix(
        `remote ${remote.name}: pulled back ${pulled.written} validated file(s) (${Math.max(1, Math.round(pulled.bytes / 1024))} KB)`,
      );
    }
    if (pulled.rejected > 0) {
      prefix(`remote ${remote.name}: blocked ${pulled.rejected} unapproved artifact path(s)`);
    }
  }

  return runResult;
}
