// Layer 2 Operational Recovery Controller (`job-queue.js remote-reset [<name>] [--clean]`):
// Terminates stuck remote job processes over `RemoteTransport` (Port), clears manifest caches, and with `--clean`
// wipes generic + plugin-declared build/scratch directories without hardcoding ecosystem-specific names in core.

import * as fs from "node:fs";
import * as path from "node:path";
import { prefix, resolveStateDirectory } from "../system.ts";
import { sanitizeRemoteLogText } from "./artifact-collector.ts";
import { EmptyRemoteRules, type ResolvedRemoteRules } from "./rules.ts";
import {
  bashQuote,
  bashWorkspaceExpr,
  RemoteLockFile,
  RemoteManifestDigestFile,
  RemoteManifestSha256File,
  RemoteWorkspaceMarkerFile,
  type RemoteSpec,
} from "./spec.ts";
import { SshTransport, type RemoteTransport } from "./transport.ts";

const BaseResetProcessNames = ["timeout", "sed"] as const;
const BaseResetCleanDirs = ["tmp", ".claude/worktrees"] as const;

function escapeAwkRegex(literal: string): string {
  return literal.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Pure builder for the remote reset bash script. Uses `remoteRules.resetProcessNames`, `remoteRules.resetCleanDirs`,
 * and `remoteRules.failureArtifacts` so core stays ecosystem-agnostic.
 */
export function buildResetScript(
  workspace: string,
  clean: boolean,
  remoteRules: ResolvedRemoteRules = EmptyRemoteRules,
): string {
  const ws = bashWorkspaceExpr(workspace);
  const processNames = [...new Set([...BaseResetProcessNames, ...remoteRules.resetProcessNames])];
  const procRegex = `^(${processNames.map(escapeAwkRegex).join("|")})$`;

  const cleanDirs = [...new Set([...BaseResetCleanDirs, ...remoteRules.resetCleanDirs])];
  const cleanSteps = clean
    ? [
        '  find -- "$WS" \\( -name bin -o -name obj \\) -type d -prune -exec rm -rf -- {} + 2>/dev/null || true',
        `  rm -rf -- ${cleanDirs.map((d) => `"$WS"/${bashQuote(d)}`).join(" ")} 2>/dev/null || true`,
      ].join("\n")
    : "";

  const artifactCleanLines = remoteRules.failureArtifacts
    .filter((rule) => rule.searchDirs.length > 0)
    .map((rule) => {
      const dirs = rule.searchDirs.map((d) => `"$WS"/${bashQuote(d)}`).join(" ");
      return `  find -- ${dirs} -name ${bashQuote(rule.findName)} -delete 2>/dev/null || true`;
    });

  return [
    "set -euo pipefail",
    `WS=${ws}`,
    "MY_PID=$$",
    "MY_PPID=$PPID",
    "KILLED=0",
    "matching_pids() {",
    `  ps -u "$(id -u)" -o pid=,comm= | PROC_REGEX=${bashQuote(procRegex)} awk '$2 ~ ENVIRON["PROC_REGEX"] {print $1}'`,
    "}",
    "lock_pids() {",
    `  if [ -d "$WS" ] && [ -f "$WS/${RemoteWorkspaceMarkerFile}" ]; then`,
    `    fuser "$WS/${RemoteLockFile}" 2>/dev/null || true`,
    "  fi",
    "}",
    "all_target_pids() {",
    "  { matching_pids; lock_pids; } | tr ' \\t' '\\n' | awk 'NF && !seen[$0]++'",
    "}",
    "for pid in $(all_target_pids); do",
    '  if [ "$pid" != "$MY_PID" ] && [ "$pid" != "$MY_PPID" ]; then',
    '    kill -TERM "$pid" 2>/dev/null && KILLED=$((KILLED + 1)) || true',
    "  fi",
    "done",
    'if [ "$KILLED" -gt 0 ]; then',
    "  sleep 1",
    "  for pid in $(all_target_pids); do",
    '    if [ "$pid" != "$MY_PID" ] && [ "$pid" != "$MY_PPID" ]; then',
    '      kill -KILL "$pid" 2>/dev/null || true',
    "    fi",
    "  done",
    "fi",
    `if [ -d "$WS" ] && [ -f "$WS/${RemoteWorkspaceMarkerFile}" ]; then`,
    `  rm -f -- "$WS/${RemoteManifestDigestFile}" "$WS/${RemoteManifestSha256File}"`,
    ...artifactCleanLines,
    cleanSteps,
    "fi",
    "MEM_MB=$(awk '/MemAvailable:/{printf \"%d\", $2/1024}' /proc/meminfo 2>/dev/null || echo '?')",
    'echo "RESET_OK killed=$KILLED mem_mb=$MEM_MB"',
  ]
    .filter(Boolean)
    .join("\n");
}

/** Resets a single remote worker over `RemoteTransport` and clears its local manifest cache. */
export async function resetWorker(
  transport: RemoteTransport,
  clean: boolean,
  stateDir: string,
  remoteRules: ResolvedRemoteRules = EmptyRemoteRules,
): Promise<{ ok: boolean; killed: string; memMb: string; error?: string }> {
  const script = buildResetScript(transport.spec.workspace, clean, remoteRules);
  const res = await transport.exec(script);
  fs.rmSync(path.join(stateDir, `remote-manifest-${transport.spec.name}.json`), { force: true });
  if (res.code !== 0) {
    return { ok: false, killed: "0", memMb: "?", error: res.stderr.trim() || `ssh exit ${res.code}` };
  }
  const summary = res.stdout
    .trim()
    .split(/\r?\n/)
    .find((l) => l.startsWith("RESET_OK "));
  return {
    ok: true,
    killed: /killed=(\d+)/.exec(summary ?? "")?.[1] ?? "0",
    memMb: /mem_mb=(\S+)/.exec(summary ?? "")?.[1] ?? "?",
  };
}

/**
 * Resets one or all configured remote CPU workers.
 * Returns exit code 0 when all selected workers succeeded, 1 otherwise.
 */
export async function remoteReset(
  remotes: readonly RemoteSpec[],
  targetName: string,
  clean: boolean,
  stateDir = resolveStateDirectory(),
  remoteRules: ResolvedRemoteRules = EmptyRemoteRules,
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
    const result = await resetWorker(new SshTransport(remote), clean, stateDir, remoteRules);
    if (!result.ok) {
      failures++;
      prefix(
        `remote-reset ${remote.name} (${remote.host}) failed: ${sanitizeRemoteLogText(result.error ?? "unknown")}`,
      );
      continue;
    }
    const cleanLabel = clean ? "yes" : "no";
    process.stdout.write(
      `remote-reset ${remote.name} (${remote.host}): ` +
        `stopped ${result.killed} process(es), clean=${cleanLabel}, MemAvailable=${result.memMb} MiB\n`,
    );
  }
  return failures === 0 ? 0 : 1;
}
