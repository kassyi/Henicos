"use strict";

// Target dialect and test project helpers for Henicos.

const fs = require("node:fs");
const path = require("node:path");

/**
 * Enumerates all test projects under tests/<category>/<name>/<name>.csproj.
 * Throws if testsDir cannot be read (caller handles error logging or reporting).
 * @param {string} testsDir - Path to the tests directory.
 * @returns {Array<{ name: string, project: string }>}
 */
function testProjectFiles(testsDir) {
  const projects = [];
  const categories = fs.readdirSync(testsDir, { withFileTypes: true }).filter((entry) => entry.isDirectory());
  for (const category of categories) {
    const categoryDir = path.join(testsDir, category.name);
    let entries = [];
    try {
      entries = fs.readdirSync(categoryDir, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const entry of entries) {
      if (entry.isDirectory()) {
        const project = path.join(categoryDir, entry.name, `${entry.name}.csproj`);
        if (fs.existsSync(project)) {
          projects.push({ name: entry.name, project });
        }
      }
    }
  }
  return projects;
}

/**
 * Removes XML comments, repeating until nothing changes so that a comment rebuilt from the pieces of removed ones
 * (`<!<!-- -->-- x -->`) is removed too.
 * @param {string} text - MSBuild XML text.
 * @returns {string}
 */
function stripXmlComments(text) {
  let current = text;
  for (;;) {
    const next = current.replace(/<!--[\s\S]*?-->/g, "");
    if (next === current) {
      return current;
    }
    current = next;
  }
}

/**
 * Reads a csproj file (ignoring XML comments) and extracts its GSharpTargetDialect.
 * Returns "cpu" or "cuda" (lowercase), or null if undefined, unreadable, or another dialect.
 * @param {string} projectFile - Absolute or relative path to .csproj.
 * @returns {"cpu" | "cuda" | null}
 */
function dialectOf(projectFile) {
  try {
    const content = stripXmlComments(fs.readFileSync(projectFile, "utf8"));
    const match = /<GSharpTargetDialect(?:\s[^>]*)?>\s*(Cpu|Cuda)\s*<\/GSharpTargetDialect>/i.exec(content);
    return match ? match[1].toLowerCase() : null;
  } catch {
    return null;
  }
}

/**
 * Reads a csproj file (ignoring XML comments) and extracts its GSharpRemoteOffload value.
 * Returns the trimmed lowercase value (e.g. "local"), or null if undefined or unreadable.
 * @param {string} projectFile - Absolute or relative path to .csproj.
 * @returns {string | null}
 */
function remoteOffloadOf(projectFile) {
  try {
    const content = stripXmlComments(fs.readFileSync(projectFile, "utf8"));
    const match = /<GSharpRemoteOffload(?:\s[^>]*)?>\s*([^\s<]+)\s*<\/GSharpRemoteOffload>/i.exec(content);
    return match ? match[1].toLowerCase() : null;
  } catch {
    return null;
  }
}

module.exports = {
  stripXmlComments,
  testProjectFiles,
  dialectOf,
  remoteOffloadOf,
};
