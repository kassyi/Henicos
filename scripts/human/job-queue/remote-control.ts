// Operational recovery CLI (`job-queue.js remote-reset [<name>] [--clean]`):
// Terminates stuck remote job processes (dotnet, VBCSCompiler, testhost, compilers, flock holders) over hardened SSH,
// clears manifest caches so the next run re-verifies from scratch, and with `--clean` wipes bin/obj/TestResults/tmp
// (and stale .claude/worktrees) to restore a CI-clean build state without touching the OS or needing a reboot.

import { spawn } from "node:child_process";
import * as fs from "node:fs";
import * as path from "node:path";
import { buildSshArgs, type RemoteSpec } from "./remotes.ts";
import {
  bashWorkspaceExpr,
  RemoteLockFile,
  RemoteManifestDigestFile,
  RemoteManifestSha256File,
} from "./remote-runner.ts";
import { prefix, resolveStateDirectory } from "./system.ts";

function runResetScript(remote: RemoteSpec, script: string): Promise<{ code: number; stdout: string; stderr: string }> {
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
    child.stdin?.end(script.replace(/\r\n/g, "\n"));
    child.on("close", (code) => resolve({ code: code ?? 1, stdout, stderr }));
  });
}

/**
 * Resets one or all configured remote CPU workers. Returns exit code 0 when all selected workers succeeded, 1 otherwise.
 */
export async function remoteReset(
  remotes: readonly RemoteSpec[],
  targetName: string,
  clean: boolean,
  stateDir = resolveStateDirectory(),
): Promise<number> {
  const selected =
    targetName !== "" ? remotes.filter((r) => r.name === targetName) : remotes.filter((r) => r.mode !== "off");
  if (selected.length === 0) {
    if (targetName !== "") {
      prefix(`no configured remote worker named "${targetName}"`);
    } else {
      prefix("no enabled remote workers configured (see tmp/job-queue/remotes.local.json)");
    }
    return 1;
  }

  let failures = 0;
  for (const remote of selected) {
    const ws = bashWorkspaceExpr(remote.workspace);
    const cleanSteps = clean
      ? [
          'find "$WS" \\( -name bin -o -name obj \\) -type d -prune -exec rm -rf -- {} + 2>/dev/null || true',
          'rm -rf -- "$WS/TestResults" "$WS/tmp" "$WS/BenchmarkDotNet.Artifacts" "$WS/.claude/worktrees" "$WS/src/Compiler/Kassyi.GSharp.Compiler/StackLinqEmbedded" 2>/dev/null || true',
        ].join("\n")
      : "";

    // Carefully kill only build/test processes owned by the current user (never sshd, never our own shell PID/PPID).
    const script = [
      "set -euo pipefail",
      `WS=${ws}`,
      "MY_PID=$$",
      "MY_PPID=$PPID",
      "KILLED=0",
      "for pid in $(ps -u \"$(id -u)\" -o pid=,comm= | awk '$2 ~ /^(dotnet|VBCSCompiler|testhost|vstest|cc1plus|clang\\+\\+|g\\+\\+|ld|lld|ninja|timeout|sed)$/ {print $1}'); do",
      '  if [ "$pid" != "$MY_PID" ] && [ "$pid" != "$MY_PPID" ]; then',
      '    kill -TERM "$pid" 2>/dev/null && KILLED=$((KILLED + 1)) || true',
      "  fi",
      "done",
      'if [ -d "$WS" ]; then',
      `  for pid in $(fuser "$WS/${RemoteLockFile}" 2>/dev/null || true); do`,
      '    if [ "$pid" != "$MY_PID" ] && [ "$pid" != "$MY_PPID" ]; then',
      '      kill -TERM "$pid" 2>/dev/null && KILLED=$((KILLED + 1)) || true',
      "    fi",
      "  done",
      "fi",
      'if [ "$KILLED" -gt 0 ]; then',
      "  sleep 1",
      "  for pid in $(ps -u \"$(id -u)\" -o pid=,comm= | awk '$2 ~ /^(dotnet|VBCSCompiler|testhost|vstest|cc1plus|clang\\+\\+|g\\+\\+|ld|lld|ninja|timeout|sed)$/ {print $1}'); do",
      '    if [ "$pid" != "$MY_PID" ] && [ "$pid" != "$MY_PPID" ]; then',
      '      kill -KILL "$pid" 2>/dev/null || true',
      "    fi",
      "  done",
      "fi",
      'if [ -d "$WS" ]; then',
      `  rm -f -- "$WS/${RemoteManifestDigestFile}" "$WS/${RemoteManifestSha256File}"`,
      '  find "$WS/tests" "$WS/src" -name "*.received.*" -delete 2>/dev/null || true',
      cleanSteps,
      "fi",
      "MEM_MB=$(awk '/MemAvailable:/{printf \"%d\", $2/1024}' /proc/meminfo 2>/dev/null || echo '?')",
      'echo "RESET_OK killed=$KILLED mem_mb=$MEM_MB"',
    ]
      .filter(Boolean)
      .join("\n");

    const res = await runResetScript(remote, script);
    // Always clear local remote manifest cache so next run re-verifies remote workspace cleanly
    fs.rmSync(path.join(stateDir, `remote-manifest-${remote.name}.json`), { force: true });

    if (res.code !== 0) {
      failures++;
      prefix(`remote-reset ${remote.name} (${remote.host}) failed: ${res.stderr.trim() || `ssh exit ${res.code}`}`);
      continue;
    }
    const summary = res.stdout
      .trim()
      .split(/\r?\n/)
      .find((l) => l.startsWith("RESET_OK "));
    const killed = /killed=(\d+)/.exec(summary ?? "")?.[1] ?? "0";
    const memMb = /mem_mb=(\S+)/.exec(summary ?? "")?.[1] ?? "?";
    process.stdout.write(
      `remote-reset ${remote.name} (${remote.host}): stopped ${killed} process(es), clean=${clean ? "yes" : "no"}, MemAvailable=${memMb} MiB\n`,
    );
  }
  return failures === 0 ? 0 : 1;
}
