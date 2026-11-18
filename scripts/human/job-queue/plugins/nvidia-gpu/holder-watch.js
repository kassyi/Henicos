'use strict';

// Holder-watch plugin for NVIDIA GPUs: composes hold sampling (holds.js) and Windows kernel pool / Event 153
// readings (pools.js) in the exact lifecycle order of gpu-lock.js.

const { createHoldWatch } = require('./holds.js');
const { createPoolWatch } = require('./pools.js');

/**
 * Creates a holder watch for `resource` at seat 0 `lockPath`.
 * `start` starts the GPU hold sampler before spawn; `afterSpawn` starts the pool query after `recordCommandProcess`;
 * `report` awaits both before lock release; `reportSync` and `stop` handle signal and exit paths.
 */
function createWatch({ lockPath, commandText, resource, mode, seat }) {
  void resource;
  void seat;
  const hold = createHoldWatch(lockPath, commandText, { mode });
  const pools = createPoolWatch(lockPath, commandText);

  return {
    start() {
      hold.start();
    },
    afterSpawn() {
      pools.start();
    },
    async report(code) {
      await Promise.all([pools.report(code), hold.report(code)]);
    },
    reportSync(code) {
      hold.reportSync(code);
    },
    stop() {
      hold.stop();
    },
  };
}

module.exports = { createWatch };
