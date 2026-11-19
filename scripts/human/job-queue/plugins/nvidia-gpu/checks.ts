// Self-tests of the nvidia-gpu plugin: the hold record (`holds.js`: sampler parsing, the `mode` column, old 11-column
// logs read as exclusive). The seat lock itself is tested by `node scripts/human/gpu-lock.js --self-test`, which
// starts processes and is not part of the unit checks.

import { createRequire } from "node:module";
import type { AssertModule } from "../../plugin-api.ts";

const require = createRequire(import.meta.url);

export function checks(assert: AssertModule): void {
  const holds = require("./holds.js") as { selfTest: () => void };
  assert.strictEqual(typeof holds.selfTest, "function", "holds.js exports selfTest");
  holds.selfTest();
}
