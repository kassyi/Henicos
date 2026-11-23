// Executes a translated CPU job on a configured remote Linux worker over SSH:
//   1. Fast delta sync (manifest diff + tar stream of changed files + deletion of removed files; 1 SSH total when 0
//      files changed, 2 SSH max when files changed — avoids ufw limit 22/tcp rate limits and minimizes handshake overhead)
//   2. Atomic execution under remote flock with a stdin-EOF lifeline watchdog (via FD 3) and FIFO stdout/stderr pumps
//      so cancel / Ctrl+C / client crash immediately kills the remote process group, while warm VBCSCompiler / MSBuild
//      daemons never hold sshd's stdout/stderr open after the command exits
//   3. Artifact pull-back of any newly generated *.received.* (on test failure) and explicit collectPaths.

import { spawn, type ChildProcess } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { RemoteSpec } from "./remotes.ts";
import { bashQuote, type RemoteCommandPlan } from "./remote-command.ts";
import {
  collectLocalManifest,
  diffManifests,
  loadRemoteManifestCache,
  parseRemoteSha256Output,
  saveRemoteManifestCache,
} from "./remote-sync.ts";
import type { ShellResult } from "./shell.ts";
import { prefix, resolveStateDirectory } from "./system.ts";

const RemoteManifestFile = ".job-queue-manifest.digest";
const RemoteLockFile = ".job-queue-slot.lock";

function tarBinary(): string {
  return process.platform === "win32" ? "tar.exe" : "tar";
}

function sshArgs(remote: RemoteSpec, remoteCommand: string): string[] {
  return [...remote.sshOptions, remote.host, remoteCommand];
}

/** Runs a short control SSH command and captures stdout/stderr. */
function runSshCapture(remote: RemoteSpec, script: string): Promise<{ code: number; stdout: string; stderr: string }> {
  return new Promise((resolve) => {
    const child = spawn("ssh", sshArgs(remote, "bash -s"), {
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });
    let stdout = "";
    let stderr = "";
    child.stdout?.on("data", (d: Buffer) => (stdout += d.toString("utf8")));
    child.stderr?.on("data", (d: Buffer) => (stderr += d.toString("utf8")));
    child.on("error", (err) => resolve({ code: 255, stdout, stderr: err.message }));
    child.stdin?.end(script.replace(/\r\n/g, "\n"));
    child.on("close", (code) => resolve({ code: code ?? 1, stdout, stderr }));
  });
}

/** Expands leading `~/` safely inside bash without quoting away tilde expansion. */
function bashWorkspaceExpr(workspace: string): string {
  if (workspace === "~") {
    return `"$HOME"`;
  }
  if (workspace.startsWith("~/")) {
    return `"$HOME/${workspace.slice(2).replace(/"/g, '\\"')}"`;
  }
  return bashQuote(workspace);
}

/** Fetches the remote workspace's current file->sha256 map when local cache is missing or out of sync. */
async function fetchRemoteManifest(remote: RemoteSpec): Promise<Record<string, string> | null> {
  const ws = bashWorkspaceExpr(remote.workspace);
  const script = [
    "set -euo pipefail",
    `WS=${ws}`,
    `mkdir -p "$WS"`,
    `cd "$WS"`,
    `find . \\( -path './.git' -o -path './tmp' -o -path './TestResults' -o -path './.vs' -o -path './${RemoteManifestFile}' -o -path './${RemoteLockFile}' -o -name 'bin' -o -name 'obj' -o -name 'node_modules' \\) -prune -o \\( -type f ! -name '*.received.*' -print0 \\) | xargs -0 -r sha256sum`,
  ].join("\n");
  const res = await runSshCapture(remote, script);
  if (res.code !== 0) {
    return null;
  }
  return parseRemoteSha256Output(res.stdout);
}

/** Streams changed files via `tar` over SSH and removes deleted files under the remote workspace flock. */
async function applyDeltaSync(
  remote: RemoteSpec,
  worktree: string,
  toUpload: string[],
  toDelete: string[],
  localDigest: string,
  expectedRemoteDigest?: string,
): Promise<{ ok: boolean; stale?: boolean; error?: string }> {
  const ws = bashWorkspaceExpr(remote.workspace);
  const guard =
    expectedRemoteDigest !== undefined
      ? `if [ "$(cat ${RemoteManifestFile} 2>/dev/null || true)" != ${bashQuote(expectedRemoteDigest)} ]; then exit 254; fi`
      : "";
  const deleteScript = toDelete.length === 0 ? "" : toDelete.map((rel) => `rm -f -- ${bashQuote(rel)}`).join("\n");

  if (toUpload.length === 0) {
    const script = [
      "set -euo pipefail",
      `WS=${ws}`,
      `mkdir -p "$WS"`,
      `exec 9>"$WS/${RemoteLockFile}"`,
      "flock 9",
      `cd "$WS"`,
      guard,
      deleteScript,
      `printf '%s\\n' ${bashQuote(localDigest)} > ${RemoteManifestFile}`,
    ]
      .filter(Boolean)
      .join("\n");
    const res = await runSshCapture(remote, script);
    if (res.code === 254) {
      return { ok: false, stale: true };
    }
    return res.code === 0 ? { ok: true } : { ok: false, error: res.stderr.trim() || `ssh exit ${res.code}` };
  }

  const listFile = path.join(os.tmpdir(), `job-queue-sync-${process.pid}-${Date.now()}.txt`);
  fs.writeFileSync(listFile, `${toUpload.join("\n")}\n`, "utf8");
  try {
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
          deleteScript,
          "tar -xf -",
          `printf '%s\\n' ${bashQuote(localDigest)} > ${RemoteManifestFile}`,
        ]
          .filter(Boolean)
          .join("\n"),
      ),
    ].join(" ");

    return await new Promise((resolve) => {
      const tar = spawn(tarBinary(), ["-cf", "-", "-T", listFile], {
        cwd: worktree,
        stdio: ["ignore", "pipe", "pipe"],
        windowsHide: true,
      });
      const ssh = spawn("ssh", sshArgs(remote, remoteCmd), {
        stdio: ["pipe", "ignore", "pipe"],
        windowsHide: true,
      });
      let errText = "";
      tar.stderr?.on("data", (d: Buffer) => (errText += d.toString("utf8")));
      ssh.stderr?.on("data", (d: Buffer) => (errText += d.toString("utf8")));
      tar.stdout?.pipe(ssh.stdin as NodeJS.WritableStream);
      let tarCode: number | null = null;
      let sshCode: number | null = null;
      const done = (): void => {
        if (tarCode !== null && sshCode !== null) {
          if (sshCode === 254) {
            resolve({ ok: false, stale: true });
          } else {
            resolve(
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
        ssh.kill();
        done();
      });
      ssh.on("error", (err) => {
        errText += err.message;
        sshCode = 255;
        tar.kill();
        done();
      });
      tar.on("close", (code) => {
        tarCode = code ?? 1;
        done();
      });
      ssh.on("close", (code) => {
        sshCode = code ?? 1;
        done();
      });
    });
  } finally {
    fs.rmSync(listFile, { force: true });
  }
}

/** Pulls back any newly created Verify `*.received.*` files and explicit `collectPaths` from the remote workspace. */
async function pullArtifacts(remote: RemoteSpec, worktree: string, collectPaths: string[]): Promise<number> {
  const ws = bashWorkspaceExpr(remote.workspace);
  const extraChecks = collectPaths
    .map((p) => `if [ -e ${bashQuote(p)} ]; then printf '%s\\0' ${bashQuote(p)}; fi`)
    .join("\n");
  const script = [
    "set -euo pipefail",
    `WS=${ws}`,
    `cd "$WS"`,
    "LIST=$(mktemp)",
    "trap 'rm -f \"$LIST\"' EXIT",
    "{ find tests src -name '*.received.*' -print0 2>/dev/null || true;",
    extraChecks,
    '} > "$LIST"',
    'if [ -s "$LIST" ]; then tar --null -T "$LIST" -cf -; fi',
  ].join("\n");

  return await new Promise((resolve) => {
    const ssh = spawn("ssh", sshArgs(remote, "bash -s"), {
      stdio: ["pipe", "pipe", "ignore"],
      windowsHide: true,
    });
    const chunks: Buffer[] = [];
    ssh.stdout?.on("data", (d: Buffer) => chunks.push(d));
    ssh.on("error", () => resolve(0));
    ssh.stdin?.end(script);
    ssh.on("close", (code) => {
      if (code !== 0 || chunks.length === 0) {
        resolve(0);
        return;
      }
      const tarData = Buffer.concat(chunks);
      if (tarData.length === 0) {
        resolve(0);
        return;
      }
      const tar = spawn(tarBinary(), ["-xf", "-"], {
        cwd: worktree,
        stdio: ["pipe", "ignore", "ignore"],
        windowsHide: true,
      });
      tar.on("error", () => resolve(0));
      tar.stdin?.end(tarData);
      tar.on("close", (tarCode) => resolve(tarCode === 0 ? tarData.length : 0));
    });
  });
}

export interface RemoteRunResult extends ShellResult {
  /** True when the remote SSH transport itself failed before executing the command (caller may fall back to local). */
  transportFailed?: boolean;
}

/**
 * Syncs `plan.worktree` to `remote`, runs `plan.bashCommand` with live stdio and a stdin-EOF lifeline watchdog,
 * and pulls back any `*.received.*` (on failure) / explicit output artifacts.
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
  const cached = loadRemoteManifestCache(remoteCachePath);

  const syncWithFetch = async (): Promise<{ uploaded: number; deleted: number; digest: string } | null> => {
    const fetched = await fetchRemoteManifest(remote);
    if (fetched === null) {
      return null;
    }
    const diff = diffManifests(localManifest, fetched);
    const applied = await applyDeltaSync(remote, plan.worktree, diff.toUpload, diff.toDelete, diff.localDigest);
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
      // Optimistically apply delta guarded by cached.digest (1 SSH call instead of 2)
      const applied = await applyDeltaSync(
        remote,
        plan.worktree,
        diff.toUpload,
        diff.toDelete,
        diff.localDigest,
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

  const runOnce = (verifyStamp: string | null): Promise<ShellResult> => {
    const ws = bashWorkspaceExpr(remote.workspace);
    const envExports = Object.entries(plan.env)
      .map(([k, v]) => `export ${k}=${bashQuote(v)}`)
      .join("\n");
    const cdSubdir = plan.remoteCwdRel !== "" ? `cd ${bashQuote(plan.remoteCwdRel)}` : "";
    const stampCheck =
      verifyStamp !== null
        ? `if [ "$(cat ${RemoteManifestFile} 2>/dev/null || true)" != ${bashQuote(verifyStamp)} ]; then exit 254; fi`
        : "";
    const execScript = [
      "set -euo pipefail",
      `WS=${ws}`,
      `exec 9>"$WS/${RemoteLockFile}"`,
      "flock 9",
      `cd "$WS"`,
      stampCheck,
      "find tests src -name '*.received.*' -delete 2>/dev/null || true",
      cdSubdir,
      'export PATH="/usr/local/bin:$HOME/.dotnet:$HOME/.cargo/bin:$PATH"',
      envExports,
      "set +e",
      // Duplicate SSH stdin onto FD 3 so the background watchdog can read it (POSIX redirects FD 0 of `&` to /dev/null).
      // Route command stdout/stderr through FIFOs drained by short-lived pumps so warm background daemons (VBCSCompiler /
      // MSBuild nodeReuse) never inherit sshd's stdout/stderr pipes and hold the SSH session open for their 10m idle timeout.
      "exec 3<&0",
      "OUT_FIFO=$(mktemp -u)",
      "ERR_FIFO=$(mktemp -u)",
      'mkfifo "$OUT_FIFO" "$ERR_FIFO"',
      'cat "$OUT_FIFO" &',
      "PUMP_OUT=$!",
      'cat "$ERR_FIFO" >&2 &',
      "PUMP_ERR=$!",
      `setsid bash -c ${bashQuote(plan.bashCommand)} </dev/null >"$OUT_FIFO" 2>"$ERR_FIFO" 3<&- 9>&- &`,
      "CMD_PID=$!",
      "(",
      "  while IFS= read -r _ <&3; do :; done",
      '  if kill -0 "$CMD_PID" 2>/dev/null; then',
      '    kill -TERM -- -"$CMD_PID" 2>/dev/null || true',
      "    sleep 1",
      '    kill -KILL -- -"$CMD_PID" 2>/dev/null || true',
      "  fi",
      ") >/dev/null 2>&1 9>&- &",
      "WATCH_PID=$!",
      'wait "$CMD_PID"',
      "RC=$?",
      'kill "$WATCH_PID" 2>/dev/null || true',
      "sleep 0.05",
      'kill "$PUMP_OUT" "$PUMP_ERR" 2>/dev/null || true',
      'rm -f "$OUT_FIFO" "$ERR_FIFO"',
      "exec 3<&-",
      "exit $RC",
    ]
      .filter(Boolean)
      .join("\n");

    return new Promise<ShellResult>((resolve) => {
      const child = spawn("ssh", sshArgs(remote, `bash -c ${bashQuote(execScript)}`), {
        // Keep stdin open as the lifeline pipe! Never call child.stdin.end() until child closes.
        stdio: ["pipe", "inherit", "inherit"],
        windowsHide: true,
        detached: process.platform !== "win32",
      });
      child.on("error", (err) => {
        prefix(`could not start ssh to ${remote.host}: ${err.message}`);
        resolve({ code: 255, signaled: false });
      });
      if (child.pid !== undefined) {
        onChild(child);
      }
      child.on("close", (code, signal) => {
        const bySignal = signal !== null || (process.platform !== "win32" && code !== null && code > 128 && code < 160);
        resolve({ code: code === null ? 1 : code, signaled: bySignal });
      });
    });
  };

  // When no files were uploaded, verify the remote stamp inside the execution SSH call itself (1 SSH total!)
  let runResult = await runOnce(uploaded === 0 && deleted === 0 ? expectedDigest : null);
  if (runResult.code === 254) {
    const res = await syncWithFetch();
    if (res === null) {
      return { code: 255, signaled: false, transportFailed: true };
    }
    prefix(`remote ${remote.name}: re-synced ${res.uploaded} changed, ${res.deleted} deleted`);
    runResult = await runOnce(null);
  }
  if (runResult.code === 255 && !runResult.signaled) {
    return { ...runResult, transportFailed: true };
  }

  // Pull back Verify *.received.* files (only possible on failure) or explicit collectPaths
  if (!runResult.signaled && (runResult.code !== 0 || plan.collectPaths.length > 0)) {
    const bytes = await pullArtifacts(remote, plan.worktree, plan.collectPaths);
    if (bytes > 0) {
      prefix(
        `remote ${remote.name}: pulled back ${Math.max(1, Math.round(bytes / 1024))} KB of output/received artifacts`,
      );
    }
  }

  return runResult;
}
