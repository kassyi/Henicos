#!/usr/bin/env node

/**
 * @file update_usings.js
 * @description Scans C# projects in src/, detects namespace/type using directives
 *              used across multiple files (default: >= 3 times), promotes them to
 *              GlobalUsings.cs, and strips redundant file-level using statements.
 */

import { readdirSync, statSync, readFileSync, writeFileSync, existsSync } from "fs";
import { resolve, join, dirname, basename } from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

// =============================================================================
// Configurations & CLI Arguments
// =============================================================================

const DEFAULT_THRESHOLD = 3;
const DEFAULT_SRC_DIR = resolve(__dirname, "../../src");

function parseCliArgs() {
  const args = process.argv.slice(2);
  const options = {
    srcDir: DEFAULT_SRC_DIR,
    threshold: DEFAULT_THRESHOLD,
    dryRun: false,
  };

  for (const arg of args) {
    if (arg.startsWith("--threshold=")) {
      const parsed = parseInt(arg.split("=")[1], 10);
      if (!Number.isNaN(parsed) && parsed > 0) {
        options.threshold = parsed;
      }
    } else if (arg === "--dry-run") {
      options.dryRun = true;
    } else if (arg.startsWith("--src=")) {
      options.srcDir = resolve(process.cwd(), arg.split("=")[1]);
    }
  }

  return options;
}

// =============================================================================
// File & Directory Utilities
// =============================================================================

/**
 * Recursively find files matching extension, ignoring obj/ and bin/ directories.
 */
function findFiles(dir, ext, fileList = []) {
  if (!existsSync(dir)) return fileList;

  const entries = readdirSync(dir);
  for (const entry of entries) {
    const fullPath = join(dir, entry);
    const stat = statSync(fullPath);

    if (stat.isDirectory()) {
      if (entry !== "bin" && entry !== "obj") {
        findFiles(fullPath, ext, fileList);
      }
    } else if (fullPath.endsWith(ext)) {
      fileList.push(fullPath);
    }
  }
  return fileList;
}

// =============================================================================
// Using Directive Parser & Processor
// =============================================================================

// Matches valid top-level C# using directives:
// e.g. "using System;", "using static System.Math;", "using Alias = System.Text;"
// Excludes local variables such as "using var x = ...;"
const USING_DIRECTIVE_REGEX =
  /^\s*using\s+(?!(?:var\s|unsafe\s*\{))(?:static\s+)?([A-Za-z0-9_.]+(?:\s*=\s*[A-Za-z0-9_.<>, ]+)?)\s*;\s*$/m;

const GLOBAL_USING_DIRECTIVE_REGEX =
  /^\s*global\s+using\s+(?:static\s+)?([A-Za-z0-9_.]+(?:\s*=\s*[A-Za-z0-9_.<>, ]+)?)\s*;\s*$/m;

function parseFileUsings(content) {
  const usings = new Set();
  const globalUsings = new Set();
  const lines = content.split(/\r?\n/);

  for (const line of lines) {
    const uMatch = USING_DIRECTIVE_REGEX.exec(line);
    if (uMatch) {
      usings.add(uMatch[1].trim());
      continue;
    }

    const gMatch = GLOBAL_USING_DIRECTIVE_REGEX.exec(line);
    if (gMatch) {
      globalUsings.add(gMatch[1].trim());
    }
  }

  return { usings: Array.from(usings), globalUsings: Array.from(globalUsings) };
}

function calculateProjectUsingFrequencies(csFiles) {
  const usingCounts = new Map();
  const allGlobalUsings = new Set();

  for (const file of csFiles) {
    const content = readFileSync(file, "utf8");
    const parsed = parseFileUsings(content);

    for (const u of parsed.usings) {
      usingCounts.set(u, (usingCounts.get(u) || 0) + 1);
    }
    for (const gu of parsed.globalUsings) {
      allGlobalUsings.add(gu);
    }
  }

  return { usingCounts, allGlobalUsings };
}

/**
 * Removes promoted or existing global usings from individual source files.
 */
function cleanSourceFile(filePath, usingsToPromote, dryRun) {
  const rawContent = readFileSync(filePath, "utf8");
  const isCrlf = rawContent.includes("\r\n");
  const eol = isCrlf ? "\r\n" : "\n";
  const lines = rawContent.split(/\r?\n/);

  const newLines = [];
  let modified = false;

  for (const line of lines) {
    const uMatch = USING_DIRECTIVE_REGEX.exec(line);
    const gMatch = GLOBAL_USING_DIRECTIVE_REGEX.exec(line);

    if (uMatch && usingsToPromote.has(uMatch[1].trim())) {
      modified = true;
      continue;
    }

    if (gMatch) {
      modified = true;
      continue;
    }

    newLines.push(line);
  }

  if (modified) {
    // Strip leading empty lines left by removed using directives
    while (newLines.length > 0 && newLines[0].trim() === "") {
      newLines.shift();
    }

    if (!dryRun) {
      writeFileSync(filePath, newLines.join(eol), "utf8");
    }
  }

  return modified;
}

/**
 * Generates the GlobalUsings.cs file content for a project.
 */
function writeGlobalUsingsFile(projDir, usingsToPromote, dryRun) {
  const globalUsingsPath = join(projDir, "GlobalUsings.cs");
  const sortedUsings = Array.from(usingsToPromote).sort();

  const content =
    "// <auto-generated />\n" +
    sortedUsings.map((u) => `global using ${u};`).join("\n") +
    "\n";

  if (!dryRun) {
    writeFileSync(globalUsingsPath, content, "utf8");
  }

  return globalUsingsPath;
}

// =============================================================================
// Main Workflow
// =============================================================================

function processProject(csprojPath, { threshold, dryRun }) {
  const projDir = dirname(csprojPath);
  const projName = basename(projDir);
  const csFiles = findFiles(projDir, ".cs");

  if (csFiles.length === 0) return;

  const { usingCounts, allGlobalUsings } = calculateProjectUsingFrequencies(csFiles);
  const usingsToPromote = new Set(
    Array.from(usingCounts.entries())
      .filter(([_, count]) => count >= threshold)
      .map(([u]) => u)
  );

  for (const gu of allGlobalUsings) {
    usingsToPromote.add(gu);
  }

  if (usingsToPromote.size === 0) return;

  console.log(`\n📦 [${projName}]`);
  console.log(`   Promoting (${usingsToPromote.size} items, threshold >= ${threshold}):`);
  for (const u of Array.from(usingsToPromote).sort()) {
    const freq = usingCounts.get(u) || 0;
    console.log(`     + global using ${u}; (${freq} files)`);
  }

  let modifiedFilesCount = 0;
  for (const file of csFiles) {
    if (basename(file) === "GlobalUsings.cs") continue;
    if (cleanSourceFile(file, usingsToPromote, dryRun)) {
      modifiedFilesCount++;
    }
  }

  writeGlobalUsingsFile(projDir, usingsToPromote, dryRun);
  console.log(`   ✓ Cleaned ${modifiedFilesCount} files and generated GlobalUsings.cs${dryRun ? " (dry-run)" : ""}`);
}

function main() {
  const options = parseCliArgs();

  console.log(`🔍 Scanning projects in: ${options.srcDir}`);
  console.log(`   Frequency threshold: >= ${options.threshold}`);
  if (options.dryRun) {
    console.log(`   Mode: DRY RUN (no files will be written)`);
  }

  const csprojFiles = findFiles(options.srcDir, ".csproj");

  if (csprojFiles.length === 0) {
    console.log("No .csproj files found.");
    return;
  }

  for (const csproj of csprojFiles) {
    processProject(csproj, options);
  }

  console.log("\n✨ Done.");
}

main();

