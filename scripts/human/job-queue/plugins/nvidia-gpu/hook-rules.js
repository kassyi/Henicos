'use strict';

// Hook rules for the built-in nvidia-gpu plugin: registers NVIDIA GPU sanitizers and profilers as runner wrappers.
//
// Usage: loaded by the hook engine plugin loader when `nvidia-gpu` is enabled in job-queue.config.json.

module.exports = {
  name: 'nvidia-gpu',
  runners: ['compute-sanitizer', 'cuda-memcheck', 'nsys', 'ncu'],
};
