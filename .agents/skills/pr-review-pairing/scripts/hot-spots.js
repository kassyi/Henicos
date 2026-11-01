#!/usr/bin/env node
// Lists the structural hot spots of C# files for a pull request review (pr-review-pairing §3): the longest methods with
// their line counts, and the switches (statements and expressions) with many cases.
// Strings and comments are masked before braces are counted, so braces inside them do not confuse the count.
// Usage:
//   node .agents/skills/pr-review-pairing/scripts/hot-spots.js <file.cs>... [--top <n>] [--min-cases <n>]
//   node .agents/skills/pr-review-pairing/scripts/hot-spots.js --changed <base> [--worktree <dir>] [--top <n>] [--min-cases <n>]
// --changed lists the .cs files that differ between <base> and HEAD of the worktree (default: this checkout).
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..', '..', '..');

const usage = `Usage:
  node .agents/skills/pr-review-pairing/scripts/hot-spots.js <file.cs>... [options]
  node .agents/skills/pr-review-pairing/scripts/hot-spots.js --changed <base> [--worktree <dir>] [options]

Options:
  --changed <base>   Review the .cs files changed between <base> and HEAD (git diff <base>...HEAD)
  --worktree <dir>   The checkout to read with --changed (default: the repository of this script)
  --top <n>          Longest methods to list per file (default 10)
  --min-cases <n>    Smallest switch to list (default 6)
  --help             Show this help`;

function parseArgs(argv) {
    const options = { files: [], changed: null, worktree: repoRoot, top: 10, minCases: 6 };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        const value = () => {
            if (i + 1 >= argv.length) {
                fail(`${arg} needs a value`);
            }
            return argv[++i];
        };
        if (arg === '--help' || arg === '-h') {
            console.log(usage);
            process.exit(0);
        } else if (arg === '--changed') {
            options.changed = value();
        } else if (arg === '--worktree') {
            options.worktree = path.resolve(value());
        } else if (arg === '--top') {
            options.top = positiveInteger(arg, value());
        } else if (arg === '--min-cases') {
            options.minCases = positiveInteger(arg, value());
        } else if (arg.startsWith('--')) {
            fail(`unknown option ${arg}`);
        } else {
            options.files.push(path.resolve(arg));
        }
    }
    if (options.changed === null && options.files.length === 0) {
        fail('give C# files or --changed <base>');
    }
    return options;
}

function positiveInteger(name, text) {
    const n = Number(text);
    if (!Number.isInteger(n) || n < 1) {
        fail(`${name} needs a positive integer, got '${text}'`);
    }
    return n;
}

function fail(message) {
    console.error(`hot-spots: ${message}\n\n${usage}`);
    process.exit(1);
}

function changedFiles(worktree, base) {
    const output = execFileSync('git', ['-C', worktree, 'diff', '--name-only', '--diff-filter=d', `${base}...HEAD`, '--', '*.cs'], {
        encoding: 'utf8',
    });
    return output
        .split('\n')
        .map((line) => line.trim())
        .filter((line) => line !== '')
        .map((line) => path.join(worktree, line));
}

// Replaces the contents of comments, strings and character literals with spaces, keeping line breaks and positions.
// Interpolation holes are masked with their string; a brace inside one never counts.
function mask(text) {
    const out = text.split('');
    let i = 0;
    const blank = (from, to) => {
        for (let k = from; k < to; k++) {
            if (out[k] !== '\n' && out[k] !== '\r') {
                out[k] = ' ';
            }
        }
    };
    while (i < text.length) {
        const c = text[i];
        const next = text[i + 1];
        if (c === '/' && next === '/') {
            const end = text.indexOf('\n', i);
            const stop = end < 0 ? text.length : end;
            blank(i, stop);
            i = stop;
        } else if (c === '/' && next === '*') {
            const end = text.indexOf('*/', i + 2);
            const stop = end < 0 ? text.length : end + 2;
            blank(i, stop);
            i = stop;
        } else if (c === '\'') {
            let k = i + 1;
            while (k < text.length && text[k] !== '\'' && text[k] !== '\n') {
                k += text[k] === '\\' ? 2 : 1;
            }
            blank(i + 1, k);
            i = k + 1;
        } else if (c === '"' || ((c === '@' || c === '$') && /[@$"]/.test(next))) {
            i = maskString(text, i, blank);
        } else {
            i++;
        }
    }
    return out.join('');
}

// Masks one string literal starting at `start` (prefixes $ and @, raw """ strings) and returns the index after it.
function maskString(text, start, blank) {
    let k = start;
    let verbatim = false;
    while (text[k] === '$' || text[k] === '@') {
        verbatim = verbatim || text[k] === '@';
        k++;
    }
    if (text[k] !== '"') {
        return start + 1;
    }
    let quotes = 0;
    while (text[k + quotes] === '"') {
        quotes++;
    }
    if (quotes >= 3) {
        const closer = '"'.repeat(quotes);
        const end = text.indexOf(closer, k + quotes);
        const stop = end < 0 ? text.length : end + quotes;
        blank(k + quotes, stop - quotes);
        return stop;
    }
    let j = k + 1;
    while (j < text.length) {
        if (!verbatim && text[j] === '\\') {
            j += 2;
        } else if (text[j] === '"') {
            if (verbatim && text[j + 1] === '"') {
                j += 2;
            } else {
                break;
            }
        } else if (!verbatim && text[j] === '\n') {
            break;
        } else {
            j++;
        }
    }
    blank(k + 1, j);
    return j + 1;
}

// Index of the character that closes the bracket opened at `open`.
function matching(code, open) {
    const opener = code[open];
    const closer = { '(': ')', '{': '}', '[': ']' }[opener];
    let depth = 0;
    for (let k = open; k < code.length; k++) {
        if (code[k] === opener) {
            depth++;
        } else if (code[k] === closer) {
            depth--;
            if (depth === 0) {
                return k;
            }
        }
    }
    return code.length - 1;
}

function lineStarts(code) {
    const starts = [0];
    for (let k = 0; k < code.length; k++) {
        if (code[k] === '\n') {
            starts.push(k + 1);
        }
    }
    return starts;
}

function lineOf(starts, index) {
    let lo = 0;
    let hi = starts.length - 1;
    while (lo < hi) {
        const mid = (lo + hi + 1) >> 1;
        if (starts[mid] <= index) {
            lo = mid;
        } else {
            hi = mid - 1;
        }
    }
    return lo + 1;
}

const modifiers = 'public|internal|private|protected|static|override|virtual|async|sealed|new|partial|abstract|extern|unsafe|readonly|file';
// A member header: modifiers, a return type (or none for a constructor), the name and the opening parenthesis.
const headerPattern = new RegExp(`^[ \\t]*(?:\\[[^\\]\\n]*\\][ \\t]*)*(?:(?:${modifiers})[ \\t]+)+[^=;(){}\\n]*?(\\b(?:operator\\s*\\S+|\\w+))\\s*(?:<[^()\\n]*>)?\\s*\\(`, 'gm');

// Methods, constructors and operators with a block or expression body.
function methods(code, starts) {
    const found = [];
    for (const match of code.matchAll(headerPattern)) {
        const name = match[1];
        // Statements that look like a header, and type declarations with a primary constructor
        if (/^(if|for|foreach|while|switch|using|lock|return|catch|when)$/.test(name)
            || /\b(class|struct|record|interface|delegate)\b/.test(match[0])) {
            continue;
        }
        const paren = match.index + match[0].length - 1;
        let k = matching(code, paren) + 1;
        let end = -1;
        let parens = 0;
        for (; k < code.length; k++) {
            const c = code[k];
            if (c === '(') {
                parens++;
            } else if (c === ')') {
                parens--;
            } else if (parens === 0 && c === '{') {
                end = matching(code, k);
                break;
            } else if (parens === 0 && c === '=' && code[k + 1] === '>') {
                end = expressionEnd(code, k + 2);
                break;
            } else if (parens === 0 && (c === ';' || c === '}')) {
                break;
            }
        }
        if (end >= 0) {
            const first = lineOf(starts, match.index + match[0].search(/\S/));
            const last = lineOf(starts, end);
            found.push({ name, first, last, lines: last - first + 1 });
        }
    }
    return found;
}

// Index of the semicolon that ends an expression body starting at `from`.
function expressionEnd(code, from) {
    let depth = 0;
    for (let k = from; k < code.length; k++) {
        const c = code[k];
        if (c === '(' || c === '{' || c === '[') {
            depth++;
        } else if (c === ')' || c === '}' || c === ']') {
            depth--;
        } else if (c === ';' && depth === 0) {
            return k;
        }
    }
    return code.length - 1;
}

// Switch statements and switch expressions with the number of their cases (statement labels or expression arms).
function switches(code, starts) {
    const found = [];
    for (const match of code.matchAll(/\bswitch\b/g)) {
        let k = match.index + match[0].length;
        while (/\s/.test(code[k])) {
            k++;
        }
        let kind = 'expression';
        if (code[k] === '(') {
            kind = 'statement';
            k = matching(code, k) + 1;
            while (/\s/.test(code[k])) {
                k++;
            }
        }
        if (code[k] !== '{') {
            continue;
        }
        const close = matching(code, k);
        const cases = kind === 'statement' ? statementCases(code, k, close) : expressionArms(code, k, close);
        const first = lineOf(starts, match.index);
        const last = lineOf(starts, close);
        found.push({ kind, first, last, cases, lines: last - first + 1 });
    }
    return found;
}

// `case` and `default:` labels directly inside the switch block (not those of a nested switch).
function statementCases(code, open, close) {
    let count = 0;
    let depth = 0;
    for (let k = open + 1; k < close; k++) {
        const c = code[k];
        if (c === '{') {
            depth++;
        } else if (c === '}') {
            depth--;
        } else if (depth === 0 && /\w/.test(c) && !/\w/.test(code[k - 1])) {
            const rest = code.slice(k, k + 12);
            if (/^case\b/.test(rest) || /^default\s*:/.test(rest)) {
                count++;
            }
        }
    }
    return count;
}

// Arms of a switch expression: the `=>` at the top level of its braces (a lambda inside an arm sits in parentheses).
function expressionArms(code, open, close) {
    let count = 0;
    let depth = 0;
    for (let k = open + 1; k < close; k++) {
        const c = code[k];
        if (c === '(' || c === '{' || c === '[') {
            depth++;
        } else if (c === ')' || c === '}' || c === ']') {
            depth--;
        } else if (depth === 0 && c === '=' && code[k + 1] === '>') {
            count++;
        }
    }
    return count;
}

function report(file, options) {
    const code = mask(fs.readFileSync(file, 'utf8'));
    const starts = lineStarts(code);
    const display = path.relative(options.worktree, file).split(path.sep).join('/');
    const longest = methods(code, starts)
        .sort((a, b) => b.lines - a.lines || a.first - b.first)
        .slice(0, options.top);
    const big = switches(code, starts)
        .filter((s) => s.cases >= options.minCases)
        .sort((a, b) => a.first - b.first);
    console.log(`== ${display}`);
    if (longest.length === 0) {
        console.log('  (no methods)');
    }
    for (const m of longest) {
        console.log(`  ${String(m.lines).padStart(4)} lines  ${m.first}-${m.last}  ${m.name}`);
    }
    for (const s of big) {
        console.log(`  switch ${s.kind} ${s.first}-${s.last}  cases=${s.cases} lines=${s.lines}`);
    }
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const files = options.changed === null ? options.files : changedFiles(options.worktree, options.changed);
    if (files.length === 0) {
        console.log('No C# files to check.');
        return;
    }
    let missing = 0;
    for (const file of files) {
        if (!fs.existsSync(file)) {
            console.error(`hot-spots: not found: ${file}`);
            missing++;
            continue;
        }
        report(file, options);
    }
    if (missing > 0) {
        process.exit(1);
    }
}

main();
