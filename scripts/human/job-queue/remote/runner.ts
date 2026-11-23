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
  const timeoutSec = Math.max(60, Math.round(options.maxJobMinutes * 60));
  const envExports = Object.entries(options.plan.env)
    .map(([k, v]) => `export ${k}=${bashQuote(v)}`)
    .join("\n");
  const cdSubdir = options.plan.remoteCwdRel !== "" ? `cd ${bashQuote(options.plan.remoteCwdRel)}` : "";
  const mkdirDirs = [
    ...new Set(options.plan.collectPaths.map((c) => (c.kind === "dir" ? c.path : path.posix.dirname(c.path)))),
  ]
    .filter((d) => d !== "" && d !== ".")
    .map((d) => `mkdir -p -- ${bashQuote(d)}`)
    .join("\n");

  const integrityCheck = options.verifyIntegrity
    ? [
        `if [ "$(cat ${RemoteManifestDigestFile} 2>/dev/null || true)" != ${bashQuote(options.expectedDigest)} ]; then exit 254; fi`,
        `if [ ! -f ${RemoteManifestSha256File} ]; then exit 254; fi`,
        `EXPECTED_LIST=$(mktemp)`,
        `ACTUAL_LIST=$(mktemp)`,
        `LC_ALL=C awk '{print substr($0, 67)}' ${RemoteManifestSha256File} | LC_ALL=C sort > "$EXPECTED_LIST"`,
        `find . ${options.pruneExpr} | tr '\\0' '\\n' | LC_ALL=C sort > "$ACTUAL_LIST"`,
        `if ! cmp -s "$EXPECTED_LIST" "$ACTUAL_LIST"; then rm -f "$EXPECTED_LIST" "$ACTUAL_LIST"; exit 254; fi`,
        `rm -f "$EXPECTED_LIST" "$ACTUAL_LIST"`,
        `if ! sha256sum --quiet -c ${RemoteManifestSha256File} >/dev/null 2>&1; then exit 254; fi`,
      ].join("\n")
    : "";

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
    "OUT_FIFO=$(mktemp -u)",
    "ERR_FIFO=$(mktemp -u)",
    'mkfifo "$OUT_FIFO" "$ERR_FIFO"',
    'exec 7<>"$OUT_FIFO" 8<>"$ERR_FIFO"',
    `sed -u -n "/^${eofToken}\$/q;p" <"$OUT_FIFO" 7<&- 8<&- 3<&- 9>&- &`,
    "PUMP_OUT=$!",
    `sed -u -n "/^${eofToken}\$/q;p" <"$ERR_FIFO" >&2 7<&- 8<&- 3<&- 9>&- &`,
    "PUMP_ERR=$!",
    `setsid timeout --kill-after=10s ${timeoutSec}s bash -c ${bashQuote(options.plan.bashCommand)} </dev/null >"$OUT_FIFO" 2>"$ERR_FIFO" 3<&- 7<&- 8<&- 9>&- &`,
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

  private runOnce(
    plan: RemoteCommandPlan,
    expectedDigest: string,
    verifyIntegrity: boolean,
    onChild: (child: ChildProcess) => void,
  ): Promise<ShellResult> {
    const script = buildExecScript({
      workspace: this.transport.spec.workspace,
      maxJobMinutes: this.transport.spec.maxJobMinutes,
      plan,
      pruneExpr: this.syncer.pruneExpr,
      expectedDigest,
      verifyIntegrity,
      remoteRules: this.remoteRules,
    });
    const outSan = new RemoteStreamSanitizer();
    const errSan = new RemoteStreamSanitizer();
    return this.transport.spawnInteractive(script, {
      onStdout: (chunk) => {
        const clean = outSan.push(chunk);
        if (clean !== "") {
          process.stdout.write(clean);
        }
      },
      onStderr: (chunk) => {
        const clean = errSan.push(chunk);
        if (clean !== "") {
          process.stderr.write(clean);
        }
      },
      onClose: () => {
        const outTail = outSan.flush();
        if (outTail !== "") {
          process.stdout.write(outTail);
        }
        const errTail = errSan.flush();
        if (errTail !== "") {
          process.stderr.write(errTail);
        }
      },
      onChild,
    });
  }

  async execute(
    plan: RemoteCommandPlan,
    onChild: (child: ChildProcess) => void,
    localManifestOverride?: Record<string, string>,
  ): Promise<RemoteRunResult> {
    const spec = this.transport.spec;
    const synced = await this.syncer.sync(plan.worktree, localManifestOverride);
    if (!synced.ok) {
      return { code: 255, signaled: false, transportFailed: true };
    }
    prefix(
      `remote ${spec.name} (${spec.host}): synced ${synced.uploaded} changed, ${synced.deleted} deleted in ${synced.elapsedSec}s`,
    );

    let runResult = await this.runOnce(plan, synced.digest, true, onChild);
    if (runResult.code === 254) {
      prefix(`remote ${spec.name}: workspace integrity mismatch detected; re-syncing clean sources`);
      const healed = await this.syncer.resyncClean(plan.worktree, synced.localManifest, synced.sha256ManifestText);
      if (healed === null) {
        return { code: 255, signaled: false, transportFailed: true };
      }
      prefix(`remote ${spec.name}: repaired ${healed.uploaded} changed, ${healed.deleted} deleted`);
      runResult = await this.runOnce(plan, synced.digest, false, onChild);
    }
    if (runResult.code === 255 && !runResult.signaled) {
      return { ...runResult, transportFailed: true };
    }

    if (!runResult.signaled && (runResult.code !== 0 || plan.collectPaths.length > 0)) {
      const pulled = await this.collector.pull(
        plan.worktree,
        runResult.code !== 0,
        manifestParentDirs(synced.localManifest),
        plan.collectPaths,
      );
      if (pulled.crashedTests.length > 0) {
        prefix(`remote ${spec.name}: test host crashed/aborted while running: ${pulled.crashedTests.join(", ")}`);
      }
      if (pulled.written > 0) {
        prefix(
          `remote ${spec.name}: pulled back ${pulled.written} validated file(s) (${Math.max(1, Math.round(pulled.bytes / 1024))} KB)`,
        );
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
): Promise<RemoteRunResult> {
  return new RemoteRunner(new SshTransport(remote), stateDir, remoteRules).execute(plan, onChild);
}
