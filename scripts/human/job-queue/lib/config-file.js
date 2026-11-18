'use strict';

// Discovers and reads job-queue.config.json from an explicit override (or JOB_QUEUE_CONFIG) or upward from the
// entry script directory (never from process.cwd()).

const fs = require('node:fs');
const path = require('node:path');

const ConfigFileName = 'job-queue.config.json';

/**
 * Normalizes a found path with fs.realpathSync.native so Windows drive letters and 8.3 short paths match.
 * Returns null when the path does not exist (ENOENT / ENOTDIR).
 * @param {string} targetPath
 * @returns {string | null}
 */
function realpathOrNull(targetPath) {
  try {
    return fs.realpathSync.native(targetPath);
  } catch (error) {
    if (error && (error.code === 'ENOENT' || error.code === 'ENOTDIR')) {
      return null;
    }
    return path.resolve(targetPath);
  }
}

/**
 * Finds job-queue.config.json.
 * Uses `overridePath` or `process.env.JOB_QUEUE_CONFIG` when non-empty (no upward search; ENOENT returns null).
 * Otherwise walks upward from `fromDirectory` (never `process.cwd()`).
 * @param {string} fromDirectory
 * @param {string} [overridePath]
 * @returns {string | null}
 */
function findConfigFile(fromDirectory, overridePath) {
  const envPath = process.env.JOB_QUEUE_CONFIG;
  const explicit =
    typeof overridePath === 'string' && overridePath !== ''
      ? overridePath
      : typeof envPath === 'string' && envPath !== ''
        ? envPath
        : '';
  if (explicit !== '') {
    return realpathOrNull(path.resolve(explicit));
  }
  let directory = path.resolve(fromDirectory);
  for (;;) {
    const candidate = path.join(directory, ConfigFileName);
    const found = realpathOrNull(candidate);
    if (found !== null) {
      return found;
    }
    const parent = path.dirname(directory);
    if (parent === directory) {
      return null;
    }
    directory = parent;
  }
}

/**
 * Loads and parses job-queue.config.json, returning status 'ok' | 'missing' | 'invalid'.
 * @param {string} fromDirectory
 * @param {string} [overridePath]
 * @returns {{ status: 'ok' | 'missing' | 'invalid', path: string | null, raw: Record<string, unknown> | null,
 *   error: string | null }}
 */
function loadConfigFile(fromDirectory, overridePath) {
  const foundPath = findConfigFile(fromDirectory, overridePath);
  if (foundPath === null) {
    return { status: 'missing', path: null, raw: null, error: null };
  }
  let text;
  try {
    text = fs.readFileSync(foundPath, 'utf8');
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return { status: 'missing', path: null, raw: null, error: null };
    }
    return {
      status: 'invalid',
      path: foundPath,
      raw: null,
      error: error instanceof Error ? error.message : String(error),
    };
  }
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch (error) {
    return {
      status: 'invalid',
      path: foundPath,
      raw: null,
      error: error instanceof Error ? error.message : String(error),
    };
  }
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return {
      status: 'invalid',
      path: foundPath,
      raw: null,
      error: 'config root must be a JSON object',
    };
  }
  return { status: 'ok', path: foundPath, raw: parsed, error: null };
}

module.exports = {
  ConfigFileName,
  findConfigFile,
  loadConfigFile,
};
