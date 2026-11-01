#!/usr/bin/env node
'use strict';

/*
 * test-doc-snippet.js
 *
 * Purpose:
 *   Node.js port of Test-DocSnippet.ps1 (usable by agents that cannot run PowerShell).
 *   Runs a C# snippet (a .cs file, or the first ```csharp block under "## Examples" of a .md file)
 *   as a DocTest program with an explicit list of assembly dependencies, proving that the
 *   snippet compiles and runs against exactly those assemblies.
 *
 * Usage:
 *   node scripts/human/test-doc-snippet.js --file-path <snippet.cs|doc.md> [--assemblies <A,B,...>]
 *   node scripts/human/test-doc-snippet.js <snippet.cs|doc.md> [--assemblies <A,B,...>]
 *   node scripts/human/test-doc-snippet.js --help
 *
 *   --file-path    Snippet file (required, may also be given as the first positional argument).
 *   --assemblies   Comma-separated assembly (project) names, e.g. "Kassyi.GSharp.Core,Kassyi.GSharp.Runtime".
 *
 * Side effects (same as the .ps1, nothing is restored afterwards):
 *   - Removes every <ItemGroup> that holds a <ProjectReference> from the DocTest csproj, then
 *     adds the requested projects through `dotnet add reference`.
 *   - Overwrites tests/Compiler/Kassyi.GSharp.DocTest/Program.cs (git-ignored) with the snippet.
 *
 * Exit codes: 0 on success, 1 on usage/lookup errors, otherwise the exit code of `dotnet run`.
 */

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

// Paths (the repo root is two levels above this file (scripts/human/)).
const repoRoot = path.resolve(__dirname, '..', '..');
const docTestDir = path.join(repoRoot, 'tests', 'Compiler', 'Kassyi.GSharp.DocTest');
const docTestProj = path.join(docTestDir, 'Kassyi.GSharp.DocTest.csproj');
const programCs = path.join(docTestDir, 'Program.cs');

const helpText = [
  'Usage: node scripts/human/test-doc-snippet.js --file-path <snippet.cs|doc.md> [--assemblies <A,B,...>]',
  '       node scripts/human/test-doc-snippet.js <snippet.cs|doc.md> [--assemblies <A,B,...>]',
  '',
  '  --file-path    Snippet file (.cs, or .md with a csharp block under "## Examples"). Required.',
  '  --assemblies   Comma-separated assembly names whose projects are referenced by the DocTest.',
  '  --help         Show this help.',
].join('\n');

/** Mirrors Write-Error + exit 1 of the .ps1: message to stderr, exit code 1. */
function fail(message) {
  process.stderr.write(message + '\n');
  process.exit(1);
}

function parseArgs(argv) {
  let filePath = null;
  let assemblies = '';
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--help' || arg === '-h') {
      process.stdout.write(helpText + '\n');
      process.exit(0);
    } else if (arg === '--file-path') {
      filePath = argv[++i];
    } else if (arg.startsWith('--file-path=')) {
      filePath = arg.slice('--file-path='.length);
    } else if (arg === '--assemblies') {
      assemblies = argv[++i] || '';
    } else if (arg.startsWith('--assemblies=')) {
      assemblies = arg.slice('--assemblies='.length);
    } else if (!arg.startsWith('--') && filePath === null) {
      filePath = arg; // first positional argument
    } else {
      fail('Unknown argument: ' + arg + '\n' + helpText);
    }
  }
  if (!filePath) {
    fail('Missing required --file-path.\n' + helpText);
  }
  return { filePath, assemblies };
}

/**
 * Removes every <ItemGroup> that contains a <ProjectReference> from the csproj text.
 * There is no XML parser in Node's built-ins, so this works on the raw text: only the matched
 * block, its leading indentation and its trailing line break are removed, so the rest of the
 * file (indentation, line endings, missing final newline) is preserved byte for byte.
 * MSBuild does not nest <ItemGroup>, so a non-greedy match up to the next closing tag is safe.
 * The file is returned unchanged (and not rewritten) when nothing matches, like the .ps1.
 */
function stripProjectReferenceGroups(text) {
  // Matches "<ItemGroup ...>...</ItemGroup>"; the lookahead excludes self-closing "<ItemGroup />".
  const group = /[ \t]*<ItemGroup(?:\s[^>]*[^/>])?\s*>[\s\S]*?<\/ItemGroup>[ \t]*(?:\r\n|\n)?/g;
  return text.replace(group, (block) => (/<ProjectReference\b/.test(block) ? '' : block));
}

/**
 * Equivalent of `fd -p "<asm>.csproj$" src`: the pattern is a regex matched against the whole
 * path (so "Core" also matches "Kassyi.GSharp.Core"). Hidden, bin, obj and node_modules
 * directories are skipped. Returns the first match in sorted order, or null.
 */
function escapeRegExp(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function findProject(asm) {
  const pattern = new RegExp(escapeRegExp(asm) + '\\.csproj$');
  const skip = new Set(['bin', 'obj', 'node_modules']);
  const walk = (dir) => {
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch (e) {
      return null;
    }
    entries.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name.startsWith('.') || skip.has(entry.name)) continue;
        const found = walk(full);
        if (found) return found;
      } else if (pattern.test(full)) {
        return full;
      }
    }
    return null;
  };
  return walk(path.join(repoRoot, 'src'));
}

/**
 * Extracts the first ```csharp block that follows a "## Examples" heading.
 * Returns the code lines, or reports the same DEBUG lines as the .ps1 and exits 1.
 */
function extractMarkdownCode(filePath) {
  const content = fs.readFileSync(filePath, 'utf8').replace(/^﻿/, '');
  const lines = content.split(/\r\n|\n|\r/);
  if (lines.length > 0 && lines[lines.length - 1] === '') lines.pop(); // trailing newline, not a line

  let inExamples = false;
  let inCodeBlock = false;
  const codeLines = [];
  for (const line of lines) {
    const trimmed = line.trim();
    if (/^## Examples/.test(trimmed)) {
      inExamples = true;
      continue;
    }
    if (inExamples) {
      if (/^```csharp/.test(trimmed)) {
        inCodeBlock = true;
        continue;
      }
      if (inCodeBlock) {
        if (/^```$/.test(trimmed)) break;
        codeLines.push(line);
      }
    }
  }

  if (codeLines.length === 0) {
    console.log('DEBUG: Total lines read = ' + lines.length);
    console.log('DEBUG: Did it find Examples? ' + (inExamples ? 'True' : 'False'));
    console.log('DEBUG: Did it find code block? ' + (inCodeBlock ? 'True' : 'False'));
    fail("No csharp code block found under '## Examples' in the markdown file.");
  }
  return codeLines;
}

function main() {
  const { filePath, assemblies } = parseArgs(process.argv.slice(2));

  if (!fs.existsSync(filePath)) {
    fail('Snippet file not found: ' + filePath);
  }

  // 1. Clean the project
  const projText = fs.readFileSync(docTestProj, 'utf8');
  const cleaned = stripProjectReferenceGroups(projText);
  if (cleaned !== projText) {
    fs.writeFileSync(docTestProj, cleaned, 'utf8');
  }

  // 2. Add References
  if (assemblies.trim() !== '') {
    const asmList = assemblies.split(',').map((s) => s.trim()).filter((s) => s !== '');
    for (const asm of asmList) {
      const projPath = findProject(asm);
      if (projPath) {
        // stdout is discarded (Out-Null in the .ps1); stderr stays visible.
        spawnSync('dotnet', ['add', docTestProj, 'reference', projPath], {
          stdio: ['ignore', 'ignore', 'inherit'],
        });
      } else {
        fail('Could not find project for assembly: ' + asm);
      }
    }
  }

  // 3. Copy or Extract Code
  if (filePath.endsWith('.md')) {
    const codeLines = extractMarkdownCode(filePath);
    // Set-Content semantics: platform line endings, a trailing newline, UTF-8 without BOM.
    const eol = process.platform === 'win32' ? '\r\n' : '\n';
    fs.writeFileSync(programCs, codeLines.join(eol) + eol, 'utf8');
  } else {
    fs.copyFileSync(filePath, programCs);
  }

  // 4. Build & Run
  const run = spawnSync('dotnet', ['run', '--project', docTestProj], { stdio: 'inherit' });
  const exitCode = run.status === null ? 1 : run.status;

  if (exitCode !== 0) {
    process.exit(exitCode);
  } else {
    const msg = 'DocTest SUCCESS. The assembly dependencies are proven.';
    console.log(process.stdout.isTTY ? '\x1b[32m' + msg + '\x1b[0m' : msg);
  }
}

main();
