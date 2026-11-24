// Layer 2 Facade: `RemoteRunner` orchestrates the 3-stage remote execution pipeline
//   1. Sync (`WorkspaceSyncer`)
//   2. Run with cryptographic pre-run integrity check + exit-254 auto-heal retry (`RemoteTransport`)
//   3. Collect validated failure artifacts & in-memory crash summaries (`ArtifactCollector`)

import type { ChildProcess } from "node:child_process";
import * as crypto from "node:crypto";
import * as path from "node:path";
import type { ShellResult } from "../shell.ts";
import { prefix, resolveStateDirectory } from "../system.ts";
import { ArtifactCollector, RemoteStreamSanitizer } from "./artifact-collector.ts";
import type { RemoteCommandPlan } from "./command-translator.ts";
import { EmptyRemoteRules, type ResolvedRemoteRules } from "./rules.ts";
import {
  bashQuote,
  bashWorkspaceExpr,
  RemoteLockFile,
  RemoteManifestDigestFile,
  RemoteManifestSha256File,
  type RemoteSpec,
} from "./spec.ts";
import { manifestParentDirs, WorkspaceSyncer } from "./syncer.ts";
import { SshTransport, type RemoteTransport } from "./transport.ts";

export interface RemoteRunResult extends ShellResult {
  /** True when the remote transport itself failed before executing the command (caller may fall back to local). */
  transportFailed?: boolean;
  /** True when the remote command hit the GNU `timeout` limit (`exit 124`). */
  timedOut?: boolean;
}

export interface ExecScriptOptions {
  workspace: string;
  maxJobMinutes: number;
  plan: RemoteCommandPlan;
  pruneExpr: string;
  expectedDigest: string;
  verifyIntegrity: boolean;
  remoteRules: ResolvedRemoteRules;
  eofToken?: string;
  execStartToken?: string;
}

function escapeSedBre(literal: string): string {
  return literal.replace(/[\\/.*[\]^$]/g, "\\$&");
}

function buildCleanupLines(remoteRules: ResolvedRemoteRules): string[] {
  const lines: string[] = [];
  for (const rule of [...remoteRules.failureArtifacts, ...remoteRules.crashSummaries]) {
    if (rule.cleanBeforeRun && rule.searchDirs.length > 0) {
      const dirs = rule.searchDirs.map((d) => bashQuote(d)).join(" ");
      lines.push(`find ${dirs} -name ${bashQuote(rule.findName)} -delete 2>/dev/null || true`);
    }
  }
  return lines;
}

/**
 * Pure builder for the remote bash execution script: flock, optional cryptographic source integrity check,
 * plugin cleanup, hard timeout, FD 3 stdin-EOF lifeline watchdog, and O_RDWR FIFO + 128-bit EOF sentinel drain.
 */
export function buildExecScript(options: ExecScriptOptions): string {
  const ws = bashWorkspaceExpr(options.workspace);
  const eofToken = options.eofToken ?? `__JQ_EOF_${crypto.randomBytes(16).toString("hex")}__`;
  const sedEof = escapeSedBre(eofToken);
  const execStartToken = options.execStartToken ?? `__JQ_START_${crypto.randomBytes(16).toString("hex")}__`;
  const timeoutSec = Math.max(60, Math.round(options.maxJobMinutes * 60));
  const envExports = Object.entries(options.plan.env)
    .map(([k, v]) => `export ${k}=${bashQuote(v)}`)
    .join("\n");
  const cdSubdir =
    options.plan.remoteCwdRel !== ""
      ? `mkdir -p -- ${bashQuote(options.plan.remoteCwdRel)}\ncd -- ${bashQuote(options.plan.remoteCwdRel)}`
      : "";
  const mkdirDirs = [
    ...new Set(options.plan.collectPaths.map((c) => (c.kind === "dir" ? c.path : path.posix.dirname(c.path)))),
  ]
    .filter((d) => d !== "" && d !== ".")
    .map((d) => `mkdir -p -- ${bashQuote(d)}`)
    .join("\n");

  const quotedDigest = bashQuote(options.expectedDigest);
  const integrityCheck = options.verifyIntegrity
    ? [
        `if [ "$(cat ${RemoteManifestDigestFile} 2>/dev/null || true)" != ${quotedDigest} ]; then`,
        "  exit 254",
        "fi",
        `if [ ! -f ${RemoteManifestSha256File} ]; then exit 254; fi`,
        "EXPECTED_LIST=$(mktemp)",
        "ACTUAL_LIST=$(mktemp)",
        `LC_ALL=C awk '{print substr($0, 67)}' ${RemoteManifestSha256File} | LC_ALL=C sort > "$EXPECTED_LIST"`,
        `find . ${options.pruneExpr} | tr '\\0' '\\n' | LC_ALL=C sort > "$ACTUAL_LIST"`,
        'if ! cmp -s "$EXPECTED_LIST" "$ACTUAL_LIST"; then rm -f "$EXPECTED_LIST" "$ACTUAL_LIST"; exit 254; fi',
        'rm -f "$EXPECTED_LIST" "$ACTUAL_LIST"',
        `if [ -s ${RemoteManifestSha256File} ] &&`,
        `  ! sha256sum --quiet -c ${RemoteManifestSha256File} >/dev/null 2>&1; then`,
        "  exit 254",
        "fi",
      ].join("\n")
    : "";

  const bashCmd = bashQuote(options.plan.bashCommand);
  return [
    "set -euo pipefail",
    `WS=${ws}`,
    `exec 9>"$WS/${RemoteLockFile}"`,
    "flock 9",
    `cd "$WS"`,
    integrityCheck,
    ...buildCleanupLines(options.remoteRules),
    mkdirDirs,
    cdSubdir,
    'export PATH="/usr/local/bin:$HOME/.dotnet:$HOME/.cargo/bin:$PATH"',
    envExports,
    "set +e",
    "exec 3<&0",
    "FIFO_DIR=$(mktemp -d)",
    'OUT_FIFO="$FIFO_DIR/out"',
    'ERR_FIFO="$FIFO_DIR/err"',
    'mkfifo "$OUT_FIFO" "$ERR_FIFO"',
    'exec 7<>"$OUT_FIFO" 8<>"$ERR_FIFO"',
    `sed -u -n "/^${sedEof}\$/q;p" <"$OUT_FIFO" 7<&- 8<&- 3<&- 9>&- &`,
    "PUMP_OUT=$!",
    `sed -u -n "/^${sedEof}\$/q;p" <"$ERR_FIFO" >&2 7<&- 8<&- 3<&- 9>&- &`,
    "PUMP_ERR=$!",
    `printf '%s\\n' ${bashQuote(execStartToken)}`,
    `setsid timeout --kill-after=10s ${timeoutSec}s bash -c ${bashCmd} ` +
      '</dev/null >"$OUT_FIFO" 2>"$ERR_FIFO" 3<&- 7<&- 8<&- 9>&- &',
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
    'rm -rf "$FIFO_DIR"',
    "exit $RC",
  ]
    .filter(Boolean)
    .join("\n");
}

/** Facade coordinating WorkspaceSyncer, RemoteTransport, and ArtifactCollector. */
export class RemoteRunner {
  private readonly transport: RemoteTransport;
  private readonly syncer: WorkspaceSyncer;
  private readonly collector: ArtifactCollector;
  private readonly remoteRules: ResolvedRemoteRules;

  constructor(
    transport: RemoteTransport,
    stateDir = resolveStateDirectory(),
    remoteRules: ResolvedRemoteRules = EmptyRemoteRules,
  ) {
    this.transport = transport;
    this.remoteRules = remoteRules;
    this.syncer = new WorkspaceSyncer(
      transport,
      { excludePrefixes: remoteRules.syncExcludePrefixes, excludeFindNames: remoteRules.syncExcludeFindNames },
      stateDir,
    );
    this.collector = new ArtifactCollector(transport, remoteRules);
  }

  private async runOnce(
    plan: RemoteCommandPlan,
    expectedDigest: string,
    verifyIntegrity: boolean,
    onChild: (child: ChildProcess) => void,
    signal?: AbortSignal,
  ): Promise<ShellResult & { execStarted: boolean }> {
    const execStartToken = `__JQ_START_${crypto.randomBytes(16).toString("hex")}__`;
    const script = buildExecScript({
      workspace: this.transport.spec.workspace,
      maxJobMinutes: this.transport.spec.maxJobMinutes,
      plan,
      pruneExpr: this.syncer.pruneExpr,
      expectedDigest,
      verifyIntegrity,
      remoteRules: this.remoteRules,
      execStartToken,
    });
    const outSan = new RemoteStreamSanitizer();
    const errSan = new RemoteStreamSanitizer();
    let execStarted = false;
    let awaitingStart = true;
    let pendingStart = "";

    const handleStdoutText = (clean: string): void => {
      if (!awaitingStart) {
        if (clean !== "") {
          process.stdout.write(clean);
        }
        return;
      }
      pendingStart += clean;
      const nlIdx = pendingStart.indexOf("\n");
      if (nlIdx >= 0) {
        const firstLine = pendingStart.slice(0, nlIdx).replace(/\r$/, "");
        const rest = pendingStart.slice(nlIdx + 1);
        awaitingStart = false;
        if (firstLine === execStartToken) {
          execStarted = true;
          pendingStart = "";
          if (rest !== "") {
            process.stdout.write(rest);
          }
        } else {
          const full = pendingStart;
          pendingStart = "";
          process.stdout.write(full);
        }
        return;
      }
      const couldMatch =
        (pendingStart.length <= execStartToken.length && execStartToken.startsWith(pendingStart)) ||
        pendingStart === `${execStartToken}\r`;
      if (!couldMatch) {
        awaitingStart = false;
        const full = pendingStart;
        pendingStart = "";
        process.stdout.write(full);
      }
    };

    const result = await this.transport.spawnInteractive(
      script,
      {
        onStdout: (chunk) => handleStdoutText(outSan.push(chunk)),
        onStderr: (chunk) => {
          const clean = errSan.push(chunk);
          if (clean !== "") {
            process.stderr.write(clean);
          }
        },
        onClose: () => {
          const outTail = outSan.flush();
          if (outTail !== "") {
            handleStdoutText(outTail);
          }
          if (awaitingStart && pendingStart !== "") {
            awaitingStart = false;
            if (pendingStart.replace(/\r$/, "") === execStartToken) {
              execStarted = true;
            } else {
              process.stdout.write(pendingStart);
            }
            pendingStart = "";
          }
          const errTail = errSan.flush();
          if (errTail !== "") {
            process.stderr.write(errTail);
          }
        },
        onChild,
      },
      signal,
    );
    return { ...result, execStarted };
  }

  async execute(
    plan: RemoteCommandPlan,
    onChild: (child: ChildProcess) => void,
    localManifestOverride?: Record<string, string>,
    signal?: AbortSignal,
  ): Promise<RemoteRunResult> {
    if (signal?.aborted) {
      return { code: 1, signaled: true };
    }
    const spec = this.transport.spec;
    const synced = await this.syncer.sync(plan.worktree, localManifestOverride, signal);
    if (!synced.ok) {
      return signal?.aborted ? { code: 1, signaled: true } : { code: 255, signaled: false, transportFailed: true };
    }
    prefix(
      `remote ${spec.name} (${spec.host}): ` +
        `synced ${synced.uploaded} changed, ${synced.deleted} deleted in ${synced.elapsedSec}s`,
    );

    if (signal?.aborted) {
      return { code: 1, signaled: true };
    }
    let attempt = await this.runOnce(plan, synced.digest, true, onChild, signal);
    if (attempt.code === 254 && !attempt.signaled && !signal?.aborted) {
      prefix(`remote ${spec.name}: workspace integrity mismatch detected; re-syncing clean sources`);
      const healed = await this.syncer.resyncClean(
        plan.worktree,
        synced.localManifest,
        synced.sha256ManifestText,
        signal,
      );
      if (signal?.aborted) {
        return { code: 1, signaled: true };
      }
      if (healed === null) {
        return { code: 255, signaled: false, transportFailed: true };
      }
      prefix(`remote ${spec.name}: repaired ${healed.uploaded} changed, ${healed.deleted} deleted`);
      if (signal?.aborted) {
        return { code: 1, signaled: true };
      }
      // Keep verifyIntegrity=true on retry so a racing workspace can never execute unverified sources.
      attempt = await this.runOnce(plan, synced.digest, true, onChild, signal);
    }

    const timedOut = attempt.code === 124 && !attempt.signaled;
    const runResult: RemoteRunResult = {
      code: attempt.code,
      signaled: attempt.signaled,
      ...(timedOut ? { timedOut: true } : {}),
    };
    if (signal?.aborted) {
      return { code: attempt.code !== 0 ? attempt.code : 1, signaled: true };
    }
    if (runResult.signaled) {
      return runResult;
    }
    if (runResult.code === 255 && !attempt.execStarted) {
      return { ...runResult, transportFailed: true };
    }
    if (timedOut) {
      prefix(`remote ${spec.name}: timed out after ${spec.maxJobMinutes}m (exit 124)`);
    }

    if (!signal?.aborted && (runResult.code !== 0 || plan.collectPaths.length > 0)) {
      const pulled = await this.collector.pull(
        plan.worktree,
        runResult.code !== 0,
        manifestParentDirs(synced.localManifest),
        plan.collectPaths,
        { knownTrackedFiles: new Set(Object.keys(synced.localManifest)), signal },
      );
      if (signal?.aborted) {
        return { code: attempt.code !== 0 ? attempt.code : 1, signaled: true };
      }
      if (pulled.crashedTests.length > 0) {
        prefix(`remote ${spec.name}: test host crashed/aborted while running: ${pulled.crashedTests.join(", ")}`);
      }
      if (pulled.written > 0) {
        const kb = Math.max(1, Math.round(pulled.bytes / 1024));
        prefix(`remote ${spec.name}: pulled back ${pulled.written} validated file(s) (${kb} KB)`);
      }
      if (pulled.rejected > 0) {
        prefix(`remote ${spec.name}: blocked ${pulled.rejected} unapproved artifact path(s)`);
      }
    }

    return runResult;
  }
}

/** Convenience entry executing `plan` on `remote` over OpenSSH. */
export function executeRemotePlan(
  remote: RemoteSpec,
  plan: RemoteCommandPlan,
  onChild: (child: ChildProcess) => void,
  stateDir = resolveStateDirectory(),
  remoteRules: ResolvedRemoteRules = EmptyRemoteRules,
  signal?: AbortSignal,
): Promise<RemoteRunResult> {
  return new RemoteRunner(new SshTransport(remote), stateDir, remoteRules).execute(plan, onChild, undefined, signal);
}
