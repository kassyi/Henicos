// Target Generator: `remotes.local.json` derived from `WorkerProfile`.
// Emits only non-mandatory SSH overrides; `job-queue/remote/spec.ts` prepends `MandatorySshOptions` at runtime.

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

/** Builds `remotes.local.json` with non-mandatory SSH timeout overrides (`spec.ts` prepends `MandatorySshOptions`). */
export function buildRemotesLocalJson(profile: WorkerProfile): RemotesLocalDocument {
  const sshTarget = `${profile.runnerUser}@${profile.connectHost}`;
  const sshOptions = ["-o", "ConnectTimeout=5"];
  const probeSshOptions = ["-o", "ConnectTimeout=3"];
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
        sshOptions,
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
