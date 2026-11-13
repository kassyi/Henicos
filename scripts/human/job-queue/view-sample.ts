// The fixed status the live view is drawn from without a scheduler: by its self-test (self-test-view.ts) and by the
// screenshots of the README (screenshot.ts). A fixed time, so that the screens are the same on every run.

import type { StatusJob, StatusReply } from "./protocol.ts";

export const Now = Date.UTC(2026, 10, 14, 5, 32, 0);
export const Minute = 60 * 1000;

export function job(id: number, fields: Partial<StatusJob>): StatusJob {
  return {
    id,
    weight: 1,
    gpu: false,
    pid: 100 + id,
    commandPid: null,
    orphan: false,
    cwd: "P:/repo",
    command: `dotnet build project-${id}`,
    queuedAt: Now - 20 * Minute,
    grantedAt: null,
    typicalMs: 10 * Minute,
    pin: null,
    before: null,
    after: null,
    keepIfFailed: false,
    gpuTurn: false,
    memoryMb: 0,
    state: "waiting",
    ahead: 0,
    position: 0,
    startAt: null,
    endAt: null,
    blockedBy: null,
    ...fields,
  };
}

/** Three running jobs (one late, one without history) and three waiting ones (one behind --after). */
export function sampleReply(): StatusReply {
  return {
    type: "status",
    pid: 1,
    now: Now,
    policy: "hrrn",
    capacity: 8,
    used: 4,
    memoryCapacity: 32000,
    memoryUsed: 6000,
    running: [
      job(812, {
        state: "running",
        gpu: true,
        weight: 2,
        memoryMb: 6144,
        command: "just test-category Runtime",
        grantedAt: Now - 7 * Minute,
        endAt: Now + 3 * Minute,
      }),
      job(815, { state: "running", grantedAt: Now - 14 * Minute, endAt: Now + Minute }), // past its typical 10 m
      job(817, { state: "running", typicalMs: null, grantedAt: Now - 6 * Minute, endAt: Now + Minute / 2 }),
    ],
    waiting: [
      job(820, { ahead: 0, position: 1, startAt: Now + 3 * Minute, endAt: Now + 6 * Minute }),
      job(821, {
        gpu: true,
        gpuTurn: true,
        ahead: 1,
        position: 2,
        startAt: Now + 4 * Minute,
        endAt: Now + 19 * Minute,
      }),
      job(822, {
        after: 821,
        blockedBy: 821,
        ahead: 2,
        position: 3,
        startAt: Now + 19 * Minute,
        endAt: Now + 49 * Minute,
      }),
    ],
  };
}
