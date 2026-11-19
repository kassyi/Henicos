#!/usr/bin/env node
'use strict';

// Thin facade over scripts/human/job-queue/plugins/nvidia-gpu/fault-watch.js so callers and CLI paths stay unchanged.

const faultWatch = require('./job-queue/plugins/nvidia-gpu/fault-watch.js');

module.exports = {
  measure: faultWatch.measure,
  measureAsync: faultWatch.measureAsync,
  compare: faultWatch.compare,
  isNonpagedHigh: faultWatch.isNonpagedHigh,
  describe: faultWatch.describe,
  warningText: faultWatch.warningText,
  startWarningText: faultWatch.startWarningText,
  DisableVariable: faultWatch.DisableVariable,
  DelayVariable: faultWatch.DelayVariable,
  DefaultGrowthWarnBytes: faultWatch.DefaultGrowthWarnBytes,
  DefaultNonpagedWarnBytes: faultWatch.DefaultNonpagedWarnBytes,
};

if (require.main === module) {
  faultWatch.main(process.argv.slice(2)).then((code) => {
    process.exitCode = code;
  });
}
