'use strict';

// Resolves the main checkout root directory (parent of git common-dir).

const path = require('node:path');
const { spawnSync } = require('node:child_process');

/**
 * Finds the main checkout directory (the parent of the git common dir) from `fromDirectory`.
 * Returns `fallbackRoot` if git fails or `fromDirectory` is outside a git repository.
 * @param {string} fromDirectory
 * @param {string} fallbackRoot
 * @returns {string}
 */
function mainCheckout(fromDirectory, fallbackRoot) {
  let base = fallbackRoot;
  const git = spawnSync('git', ['rev-parse', '--path-format=absolute', '--git-common-dir'], {
    cwd: fromDirectory,
    encoding: 'utf8',
    windowsHide: true,
  });
  if (!git.error && git.status === 0) {
    const commonDir = git.stdout.trim();
    if (commonDir !== '' && path.isAbsolute(commonDir)) {
      base = path.dirname(path.resolve(commonDir));
    }
  }
  return base;
}

module.exports = {
  mainCheckout,
};
