// Self-tests of the nvidia-gpu plugin: the hold record (`holds.js`: sampler parsing, the `mode` column, old 11-column
// logs read as exclusive), and the hook runners of `hook-rules.js` through `hook-checks.js`. The seat lock itself is
// tested by `node scripts/human/gpu-lock.js --self-test`, which starts processes and is not part of the unit checks.

import { createRequire } from "node:module";
import type { AssertModule } from "../../plugin-api.ts";

const require = createRequire(import.meta.url);

export function checks(assert: AssertModule): void {
  const holds = require("./holds.js") as { selfTest: () => void };
  assert.strictEqual(typeof holds.selfTest, "function", "holds.js exports selfTest");
  holds.selfTest();
  const { createEngine } = require("../../../../agent/check-queued-command.js") as { createEngine: unknown };
  const { checks: hookChecks } = require("./hook-checks.js") as {
    checks: (assert: AssertModule, options: { createEngine: unknown }) => void;
  };
  hookChecks(assert, { createEngine });
}
