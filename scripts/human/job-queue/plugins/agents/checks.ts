// Self-tests of the agents plugin: verifies PreToolUse payload parsing and formatting via hook-checks.js.

import { createRequire } from "node:module";
import type { AssertModule } from "../../plugin-api.ts";

const require = createRequire(import.meta.url);

export async function checks(assert: AssertModule): Promise<void> {
  const { createEngine } = require("../../../../agent/check-queued-command.js") as {
    createEngine: unknown;
  };
  const { checks: hookChecks } = require("./hook-checks.js") as {
    checks: (assert: AssertModule, options: { createEngine: unknown }) => void;
  };
  hookChecks(assert, { createEngine });
}
