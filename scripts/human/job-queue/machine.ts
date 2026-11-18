// The defaults that follow the machine (MachineProfile): the slots from its cores and the memory pool for test runs
// from its memory, parameterized by `pools` from job-queue.config.json.
//
// Measured on 24 cores / 64 GB (a mix of 4 forced rebuilds and 4 test runs of Tests.Cpu): 8 slots (3 cores each) with
// a test run weighing 1 slot finished soonest; 6 slots with test runs weighing 2 left a quarter of the CPU idle. Each
// test run took about its budget in memory. So a test run takes one slot, and the memory pool, not the slots, keeps
// test runs from running out of memory on a smaller machine.

import * as os from "node:os";
import type { PoolsConfig } from "./config.ts";

/** Default cores a slot stands for when `pools.coresPerSlot` is not configured. */
export const CoresPerSlot = 3;
/** Default minimum slots when `pools.minimumSlots` is not configured. */
export const MinimumSlots = 2;
/** Default share of physical memory for test budgets when `pools.memoryShare` is not configured. */
export const MemoryPoolShare = 0.5;

/** The machine the queue runs on: its cores, its physical memory, and configured pool ratios. */
export class MachineProfile {
  readonly cores: number;
  readonly totalBytes: number;
  readonly pools: PoolsConfig;

  constructor(cores = os.availableParallelism(), totalBytes = os.totalmem(), pools: PoolsConfig = {}) {
    this.cores = cores;
    this.totalBytes = totalBytes;
    this.pools = pools;
  }

  /** The default slots: one per coresPerSlot cores, at least minimumSlots. */
  get slots(): number {
    const coresPerSlot = this.pools.coresPerSlot ?? CoresPerSlot;
    const minimumSlots = this.pools.minimumSlots ?? MinimumSlots;
    return Math.max(minimumSlots, Math.round(this.cores / coresPerSlot));
  }

  /** The default memory pool in MB: memoryShare of the physical memory. */
  get memoryPoolMb(): number {
    const memoryShare = this.pools.memoryShare ?? MemoryPoolShare;
    return Math.floor((this.totalBytes / (1024 * 1024)) * memoryShare);
  }
}
