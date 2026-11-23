// Layer 4 Port & Adapter for remote execution transport:
// `RemoteTransport` abstracts every SSH I/O boundary (control script, binary artifact pull, tar delta upload, and
// interactive command streaming) so upper layers (syncer, runner, controller) depend only on this port and can be
// exercised offline with a fake transport in unit tests.

import { spawn, type ChildProcess } from "node:child_process";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import type { ShellResult } from "../shell.ts";
import { prefix } from "../system.ts";
import { bashQuote, buildSshArgs, type RemoteSpec } from "./spec.ts";

export interface CaptureResult {
  code: number;
  stdout: string;
  stderr: string;
}

export interface StreamCallbacks {
  onStdout(chunk: Buffer): void;
  onStderr(chunk: Buffer): void;
  onClose?(): void;
  onChild(child: ChildProcess): void;
}

/** Port abstracting all remote worker transport I/O. */
export interface RemoteTransport {
  readonly spec: RemoteSpec;
  /** Runs a short bash script via stdin and captures UTF-8 stdout/stderr. */
  exec(script: string): Promise<CaptureResult>;
  /** Runs a bash script via stdin and captures raw binary stdout up to `maxBytes` (returns null on error/overflow). */
  execBinary(script: string, maxBytes: number): Promise<Buffer | null>;
  /** Streams framing headers and optional tar archive of `files` (relative to `worktree`) to `remoteCommand`. */
  streamUpload(
    remoteCommand: string,
    headerChunks: readonly Buffer[],
    worktree: string,
    files: readonly string[],
  ): Promise<{ code: number; stderr: string }>;
  /** Runs an interactive bash script with stdin kept open as lifeline and live stdout/stderr callbacks. */
  spawnInteractive(script: string, callbacks: StreamCallbacks): Promise<ShellResult>;
}

function tarBinary(): string {
  return process.platform === "win32" ? "tar.exe" : "tar";
}

/** OpenSSH adapter implementing `RemoteTransport` with mandatory hardening flags. */
export class SshTransport implements RemoteTransport {
  readonly spec: RemoteSpec;

  constructor(spec: RemoteSpec) {
    this.spec = spec;
  }

  exec(script: string): Promise<CaptureResult> {
    return new Promise((resolve) => {
      const child = spawn("ssh", buildSshArgs(this.spec, "bash -s"), {
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

  execBinary(script: string, maxBytes: number): Promise<Buffer | null> {
    return new Promise((resolve) => {
      const child = spawn("ssh", buildSshArgs(this.spec, "bash -s"), {
        stdio: ["pipe", "pipe", "ignore"],
        windowsHide: true,
      });
      const chunks: Buffer[] = [];
      let total = 0;
      let aborted = false;
      child.stdout?.on("data", (d: Buffer) => {
        total += d.length;
        if (total > maxBytes) {
          aborted = true;
          child.kill();
          return;
        }
        chunks.push(d);
      });
      child.on("error", () => resolve(null));
      child.stdin?.on("error", () => {});
      child.stdin?.end(script.replace(/\r\n/g, "\n"));
      child.on("close", (code) => {
        if (aborted || code !== 0 || chunks.length === 0) {
          resolve(null);
          return;
        }
        resolve(Buffer.concat(chunks));
      });
    });
  }

  async streamUpload(
    remoteCommand: string,
    headerChunks: readonly Buffer[],
    worktree: string,
    files: readonly string[],
  ): Promise<{ code: number; stderr: string }> {
    if (files.length === 0) {
      return await new Promise((resolve) => {
        const ssh = spawn("ssh", buildSshArgs(this.spec, remoteCommand), {
          stdio: ["pipe", "ignore", "pipe"],
          windowsHide: true,
        });
        let stderr = "";
        ssh.stderr?.on("data", (d: Buffer) => (stderr += d.toString("utf8")));
        ssh.on("error", (err) => resolve({ code: 255, stderr: err.message }));
        ssh.stdin?.on("error", () => {});
        for (const chunk of headerChunks) {
          ssh.stdin?.write(chunk);
        }
        ssh.stdin?.end();
        ssh.on("close", (code) => resolve({ code: code ?? 1, stderr }));
      });
    }

    const listFile = path.join(os.tmpdir(), `job-queue-sync-${process.pid}-${Date.now()}.txt`);
    fs.writeFileSync(listFile, `${files.join("\n")}\n`, "utf8");
    try {
      return await new Promise((resolve) => {
        const tar = spawn(tarBinary(), ["-cf", "-", "-T", listFile], {
          cwd: worktree,
          stdio: ["ignore", "pipe", "pipe"],
          windowsHide: true,
        });
        const ssh = spawn("ssh", buildSshArgs(this.spec, remoteCommand), {
          stdio: ["pipe", "ignore", "pipe"],
          windowsHide: true,
        });
        let stderr = "";
        tar.stderr?.on("data", (d: Buffer) => (stderr += d.toString("utf8")));
        ssh.stderr?.on("data", (d: Buffer) => (stderr += d.toString("utf8")));
        const sshIn = ssh.stdin as NodeJS.WritableStream;
        sshIn.on("error", () => {});
        for (const chunk of headerChunks) {
          sshIn.write(chunk);
        }
        tar.stdout?.pipe(sshIn);

        let tarCode: number | null = null;
        let sshCode: number | null = null;
        let settled = false;
        const finish = (result: { code: number; stderr: string }): void => {
          if (!settled) {
            settled = true;
            resolve(result);
          }
        };
        const maybeDone = (): void => {
          if (tarCode !== null && sshCode !== null) {
            const code = sshCode !== 0 ? sshCode : tarCode;
            finish({ code, stderr: stderr.trim() || (code !== 0 ? `tar=${tarCode} ssh=${sshCode}` : "") });
          }
        };
        tar.on("error", (err) => {
          stderr += err.message;
          tarCode = 1;
          ssh.stdin?.destroy();
          ssh.kill();
          maybeDone();
        });
        ssh.on("error", (err) => {
          stderr += err.message;
          sshCode = 255;
          tar.stdout?.destroy();
          tar.stderr?.destroy();
          tar.kill();
          maybeDone();
        });
        tar.on("close", (code) => {
          tarCode = code ?? 1;
          if (tarCode !== 0 && sshCode === null) {
            ssh.stdin?.destroy();
            ssh.kill();
          }
          maybeDone();
        });
        ssh.on("close", (code) => {
          sshCode = code ?? 1;
          if (tarCode === null) {
            tar.stdout?.destroy();
            tar.stderr?.destroy();
            tar.kill();
          }
          if (sshCode === 254) {
            finish({ code: 254, stderr });
            return;
          }
          maybeDone();
        });
      });
    } finally {
      fs.rmSync(listFile, { force: true });
    }
  }

  spawnInteractive(script: string, callbacks: StreamCallbacks): Promise<ShellResult> {
    return new Promise((resolve) => {
      const child = spawn("ssh", buildSshArgs(this.spec, `bash -c ${bashQuote(script)}`), {
        stdio: ["pipe", "pipe", "pipe"],
        windowsHide: true,
        detached: process.platform !== "win32",
      });
      child.stdout?.on("data", (d: Buffer) => callbacks.onStdout(d));
      child.stderr?.on("data", (d: Buffer) => callbacks.onStderr(d));
      child.on("error", (err) => {
        prefix(`could not start ssh to ${this.spec.host}: ${err.message}`);
        resolve({ code: 255, signaled: false });
      });
      if (child.pid !== undefined) {
        callbacks.onChild(child);
      }
      child.on("close", (code, signal) => {
        callbacks.onClose?.();
        const bySignal = signal !== null || (process.platform !== "win32" && code !== null && code > 128 && code < 160);
        resolve({ code: code === null ? 1 : code, signaled: bySignal });
      });
    });
  }
}
