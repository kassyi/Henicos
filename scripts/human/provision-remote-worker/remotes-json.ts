// Target Generator: `remotes.local.json` derived from `WorkerProfile`, sharing `MandatorySshOptions` from
// `job-queue/remote/spec.ts` as the single source of truth.

import { MandatorySshOptions } from "../job-queue/remote/spec.ts";
import type { WorkerProfile } from "./profile.ts";

export interface RemotesLocalDocument {
  remotes: Record<
    string,
    {
      host: string;
      workspace: string;
      slots: number;
      cpus: number;
      memoryMb: number;
      mode: "prefer-remote";
      maxJobMinutes: number;
      sshOptions: string[];
      keepBuildServersWarm: boolean;
      probe: {
        argv: string[];
        min: number;
        timeoutMs: number;
      };
    }
  >;
}

/** Single-source SSH hardening options for `remotes.local.json` (reusing MandatorySshOptions from job-queue). */
export function buildRemotesLocalJson(profile: WorkerProfile): RemotesLocalDocument {
  const sshTarget = `${profile.runnerUser}@${profile.connectHost}`;
  const hardenedSshOptions = [...MandatorySshOptions, "-o", "ConnectTimeout=5"];
  const probeSshOptions = [...MandatorySshOptions.slice(0, -4), "-o", "ConnectTimeout=3"];
  return {
    remotes: {
      [profile.workerName]: {
        host: sshTarget,
        workspace: profile.workspace,
        slots: 1,
        cpus: profile.cpus,
        memoryMb: profile.memoryMb,
        mode: "prefer-remote",
        maxJobMinutes: profile.maxJobMinutes,
        sshOptions: hardenedSshOptions,
        keepBuildServersWarm: true,
        probe: {
          argv: [
            "ssh",
            ...probeSshOptions,
            "--",
            sshTarget,
            "awk '/MemAvailable:/{printf \"%d\\n\", $2/1024}' /proc/meminfo",
          ],
          min: 1536,
          timeoutMs: 5000,
        },
      },
    },
  };
}
