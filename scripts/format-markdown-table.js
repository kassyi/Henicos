#!/usr/bin/env node
'use strict';

/*
 * format-markdown-table.js
 *
 * Node.js port of scripts/Format-MarkdownTable.ps1 (dependency-free, CommonJS).
 *
 * Compacts Markdown tables: trims every cell, rewrites rows as '| a | b |' and
 * minimizes alignment rows (:---, :---:, ---:, ---). Lines inside code fences
 * (``` or ~~~) are left untouched. A file is rewritten only when at least one
 * line changed; it is saved as UTF-8 without BOM and keeps its line endings
 * (CRLF when the file contains any CRLF, LF otherwise).
 *
 * Usage:
 *   node scripts/format-markdown-table.js [path]
 *   node scripts/format-markdown-table.js --path docs/spec/en/foo.md
 *   node scripts/format-markdown-table.js --help
 *
 * path: a Markdown file or a directory scanned recursively for *.md
 *       (default: docs/spec, relative to the current directory).
 *
 * Exit code: 0 on success (also when the path does not exist, after a
 * warning on stderr), 1 on I/O failure, 2 on invalid arguments.
 */

const fs = require('fs');
const path = require('path');

const USAGE = [
  'Usage: node scripts/format-markdown-table.js [path]',
  '       node scripts/format-markdown-table.js --path <path>',
  '',
  'Compacts Markdown tables in a file, or in every *.md under a directory.',
  '',
  'Options:',
  '  -p, --path <path>  Target Markdown file or directory (default: docs/spec)',
  '  -h, --help         Show this help',
].join('\n');

// .NET String.Trim() whitespace set (differs from JS trim(): includes U+0085, excludes U+FEFF).
const TRIM_RE = new RegExp('^[\t-\r \u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+|[\t-\r \u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+$', 'g');

function dotnetTrim(s) {
  return s.replace(TRIM_RE, '');
}

function color(text, code) {
  return process.stdout.isTTY ? `\x1b[${code}m${text}\x1b[0m` : text;
}

function parseArgs(argv) {
  let target = null;
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '-h' || a === '--help') return { help: true };
    if (a === '-p' || a === '--path') {
      if (i + 1 >= argv.length) return { error: `Option '${a}' requires a value.` };
      target = argv[++i];
    } else if (a.startsWith('--path=')) {
      target = a.slice('--path='.length);
    } else if (a.startsWith('-') && a.length > 1) {
      return { error: `Unknown option '${a}'.` };
    } else {
      if (target !== null) return { error: `Unexpected argument '${a}'.` };
      target = a;
    }
  }
  return { target: target === null ? 'docs/spec' : target };
}

// Case-insensitive ordinal comparison (NTFS-like enumeration order).
function compareNames(a, b) {
  const x = a.toUpperCase();
  const y = b.toUpperCase();
  return x < y ? -1 : x > y ? 1 : 0;
}

// Mirrors Get-ChildItem -Filter *.md -Recurse -File: files of a directory first, then each subdirectory.
function collectMarkdownFiles(dir, out) {
  const mdRe = process.platform === 'win32' ? /\.md$/i : /\.md$/;
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  const files = entries.filter((e) => !e.isDirectory() && mdRe.test(e.name)).map((e) => e.name);
  const dirs = entries.filter((e) => e.isDirectory()).map((e) => e.name);
  files.sort(compareNames);
  dirs.sort(compareNames);
  for (const f of files) out.push(path.join(dir, f));
  for (const d of dirs) collectMarkdownFiles(path.join(dir, d), out);
}

// Returns the formatted table row, or null when the line is not a table row.
function formatRow(trimmed) {
  if (!(trimmed.startsWith('|') && trimmed.endsWith('|') && trimmed.length > 1)) return null;

  // Split on unescaped pipes only.
  const rawCells = trimmed.split(/(?<!\\)\|/);
  if (!(rawCells.length >= 3 && rawCells[0] === '' && rawCells[rawCells.length - 1] === '')) return null;

  const cells = [];
  for (let i = 1; i < rawCells.length - 1; i++) cells.push(dotnetTrim(rawCells[i]));

  // Delimiter row: every cell is empty or :---, ---:, :---:, --- (and not all empty).
  let isDelimiter = cells.every((c) => /^(:?-+:?|)$/.test(c));
  if (cells.length === 0 || cells.join('') === '') isDelimiter = false;

  if (isDelimiter) {
    const norm = cells.map((c) => {
      if (/^:-+:$/.test(c)) return ':---:';
      if (/^:-+$/.test(c)) return ':---';
      if (/^-+:$/.test(c)) return '---:';
      if (/^-+$/.test(c)) return '---';
      return c;
    });
    return '| ' + norm.join(' | ') + ' |';
  }
  return '| ' + cells.join(' | ') + ' |';
}

function formatContent(content) {
  const lines = content.split(/\r?\n/);
  const newLines = [];
  let inCodeBlock = false;
  let modified = false;

  for (const line of lines) {
    const trimmed = dotnetTrim(line);

    // Toggle code block state (``` or ~~~; any fence closes any fence)
    if (/^(`{3,}|~{3,})/.test(trimmed)) {
      inCodeBlock = !inCodeBlock;
      newLines.push(line);
      continue;
    }
    if (inCodeBlock) {
      newLines.push(line);
      continue;
    }

    const formatted = formatRow(trimmed);
    if (formatted !== null) {
      if (formatted !== line) modified = true;
      newLines.push(formatted);
    } else {
      newLines.push(line);
    }
  }
  // [WHY] The PowerShell original joined with LF and turned every CRLF file it touched into LF
  return modified ? newLines.join(content.includes('\r\n') ? '\r\n' : '\n') : null;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    process.stdout.write(USAGE + '\n');
    return 0;
  }
  if (args.error) {
    process.stderr.write(`${args.error}\n${USAGE}\n`);
    return 2;
  }

  const target = args.target;
  if (!fs.existsSync(target)) {
    process.stderr.write(`WARNING: Target path '${target}' does not exist.\n`);
    return 0;
  }

  const files = [];
  if (fs.statSync(target).isDirectory()) {
    collectMarkdownFiles(path.resolve(target), files);
  } else {
    files.push(path.resolve(target));
  }

  let totalModified = 0;
  let exitCode = 0;
  for (const file of files) {
    try {
      let content = fs.readFileSync(file).toString('utf8');
      // ReadAllText consumes the UTF-8 BOM, so rewritten files lose it.
      if (content.charCodeAt(0) === 0xfeff) content = content.slice(1);
      const result = formatContent(content);
      if (result !== null) {
        fs.writeFileSync(file, result, 'utf8');
        process.stdout.write(color(`Compacted: ${file}`, 32) + '\n');
        totalModified++;
      }
    } catch (err) {
      process.stderr.write(`ERROR: ${file}: ${err.message}\n`);
      exitCode = 1;
    }
  }

  process.stdout.write(color(`Finished table compaction. Modified ${totalModified} file(s).`, 36) + '\n');
  return exitCode;
}

process.exitCode = main();
