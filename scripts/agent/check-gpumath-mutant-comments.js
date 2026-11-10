#!/usr/bin/env node
// Verifies that the source line citations in the comments of the GpuMath tests
// (tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests) still point at the code they describe in
// src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.
//
// Citation forms: `File.cs:N` or `File.cs:N-M` (sets the file for what follows), `:N`, `LN`, `line N`,
// and the continuations `/ N` and `(...), N` right after a citation. A comment without a file name uses the
// last `File.cs` named earlier in the same file.
//
// Match rules, applied to the cited line or range only (no surrounding lines):
//   exact       the backtick quote, whitespace-normalized, is in the cited text; `...` in a quote matches
//               anything, and a trailing `;` may be left out.
//   FUZZY       accepted, but printed on every run so that it stays visible:
//     qualifier the quote leaves out a receiver (`Type.`) or a numeric cast that the cited line has;
//               the quote itself must not name a receiver that the line lacks.
//     helper    the clause names a helper in parentheses after the quote (`ax -= a` (DeviceAddSub(...)))
//               and that helper is called on the cited line.
//     shift     a shift shorthand (`>> 1`, `<< n`) for a `Shift*` helper call on the cited line with the
//               same operand.
// A quote after `after` (`after \`x\``) is context: it may be on the line before the cited one.
// A citation without a quote must resolve to a code line (not blank, a comment or a lone `}`), and a few
// prose words (ThrowIfNull, Array.Copy, PadTo, WriteLimbs, memset, throw removed) must be on it.
//
// Usage (from anywhere in the repository):
//   node scripts/agent/check-gpumath-mutant-comments.js [--root <repo-root>] [--verbose]
// Exits 1 on any mismatch, 2 on a usage error.
'use strict';

const fs = require('fs');
const path = require('path');

function printHelp() {
    console.log([
        'Usage: node scripts/agent/check-gpumath-mutant-comments.js [--root <dir>] [--verbose] [--help]',
        '',
        'Options:',
        '  --root <dir>  Repository root to check (default: resolved from script path)',
        '  --verbose     Print exact matches as well as FUZZY matches and mismatches',
        '  --help        Show this help message',
    ].join('\n'));
}

function parseArgs(argv) {
    const options = { repoRoot: path.resolve(__dirname, '..', '..'), verbose: false };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            printHelp();
            process.exit(0);
        } else if (arg === '--verbose') {
            options.verbose = true;
        } else if (arg === '--root') {
            if (i + 1 >= argv.length) {
                console.error('Error: --root requires a directory argument.');
                process.exit(2);
            }
            options.repoRoot = path.resolve(argv[++i]);
        } else {
            console.error(`Error: unknown option '${arg}'. Run with --help for usage.`);
            process.exit(2);
        }
    }
    return options;
}

function listCsFiles(dir, out = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (entry.name === 'bin' || entry.name === 'obj') {
            continue;
        }
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            listCsFiles(full, out);
        } else if (entry.name.endsWith('.cs')) {
            out.push(full);
        }
    }
    return out;
}

function readLines(file) {
    return fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/);
}

// ---------------------------------------------------------------------------------------------
// Product files

function createProductIndex(repoRoot, srcRoot) {
    const files = listCsFiles(srcRoot);
    const linesCache = new Map();
    return {
        resolve(spec) {
            const normalized = spec.replace(/\\/g, '/');
            if (normalized.startsWith('src/')) {
                const full = path.join(repoRoot, normalized);
                return fs.existsSync(full) ? full : null;
            }
            const matches = files.filter(f => {
                const rel = path.relative(srcRoot, f).replace(/\\/g, '/');
                return rel === normalized || path.basename(f) === normalized || rel.endsWith('/' + normalized);
            });
            return matches.length === 1 ? matches[0] : null;
        },
        lines(full) {
            if (!linesCache.has(full)) {
                linesCache.set(full, readLines(full));
            }
            return linesCache.get(full);
        },
    };
}

// ---------------------------------------------------------------------------------------------
// Parsing the citations of a test file

function maskBackticks(text) {
    return text.replace(/`[^`]*`/g, m => ' '.repeat(m.length));
}

// Consecutive `//` lines (not `///`) form one comment block.
function extractBlocks(lines) {
    const blocks = [];
    let current = [];
    for (let i = 0; i < lines.length; i++) {
        const trimmed = lines[i].trim();
        if (trimmed.startsWith('//') && !trimmed.startsWith('///')) {
            current.push({ lineNo: i + 1, text: trimmed });
        } else if (current.length > 0) {
            blocks.push(current);
            current = [];
        }
    }
    if (current.length > 0) {
        blocks.push(current);
    }
    return blocks;
}

function lineRange(startText, endText) {
    const startLine = Number(startText);
    return { startLine, endLine: endText ? Number(endText) : startLine };
}

// `File.cs:N[-M]`: also makes File.cs the file of the citations that follow.
function parseFileCitations(masked) {
    return [...masked.matchAll(/([A-Za-z0-9_./\\]+\.cs):(\d+)(?:-(\d+))?/g)]
        .map(m => ({ index: m.index, length: m[0].length, fileSpec: m[1], ...lineRange(m[2], m[3]) }));
}

// `:N[-M]` after a separator, in the current file.
function parseColonCitations(masked) {
    return [...masked.matchAll(/(?<=[\s,;(/:]|^):(\d+)(?:-(\d+))?/g)]
        .map(m => ({ index: m.index, length: m[0].length, ...lineRange(m[1], m[2]) }));
}

// `LN[-M]`, in the current file.
function parseLCitations(masked) {
    return [...masked.matchAll(/\bL(\d+)(?:-(\d+))?\b/g)]
        .map(m => ({ index: m.index, length: m[0].length, ...lineRange(m[1], m[2]) }));
}

// `line N[-M]`, in the current file.
function parseLineWordCitations(masked) {
    return [...masked.matchAll(/\bline\s+(\d+)(?:-(\d+))?\b/g)]
        .map(m => ({ index: m.index, length: m[0].length, ...lineRange(m[1], m[2]) }));
}

const ContinuationPattern = new RegExp(
    '^(\\s*\\/\\s*|\\s*\\([^)]*\\)\\s*,\\s*)(\\d+)(?:-(\\d+))?' +
    '(?!\\s*(?:words|bits|limbs|blocks|threads|iterations|ul|u\\b|L\\b))'
);

// `/ N` or `(...), N` right after a citation: the same file, another line.
function parseContinuations(masked, from) {
    const found = [];
    let pos = from;
    for (;;) {
        const cont = masked.slice(pos).match(ContinuationPattern);
        if (!cont) {
            return found;
        }
        found.push({
            index: pos + cont[1].length,
            length: cont[0].length - cont[1].length,
            ...lineRange(cont[2], cont[3]),
        });
        pos += cont[0].length;
    }
}

const CitationForms = [parseFileCitations, parseColonCitations, parseLCitations, parseLineWordCitations];

// The citations of one comment line in order, each with its file; updates context.file.
function parseLineCitations(masked, context) {
    const primary = CitationForms
        .flatMap(form => form(masked))
        .sort((a, b) => a.index - b.index);
    const citations = [];
    let consumedTo = 0;
    for (const c of primary) {
        if (c.index < consumedTo) {
            continue;
        }
        if (c.fileSpec) {
            context.file = c.fileSpec;
        }
        const run = [c, ...parseContinuations(masked, c.index + c.length)];
        for (const r of run) {
            citations.push({
                fileSpec: context.file,
                startLine: r.startLine,
                endLine: r.endLine,
                offset: r.index,
                endOffset: r.index + r.length,
            });
        }
        const last = run[run.length - 1];
        consumedTo = last.index + last.length;
    }
    return citations;
}

// The comment line plus the following lines of the block until a line with a citation or a new mutant item.
function collectClauseText(block, idx) {
    let text = block[idx].text;
    for (let look = idx + 1; look < block.length; look++) {
        const nextText = block[look].text;
        const nextMasked = maskBackticks(nextText);
        const hasCitation = CitationForms.some(form => form(nextMasked).length > 0);
        if (hasCitation || /\/\/\s*(?:mutant|equivalent):/.test(nextText) || /^\/\/\s*-\s/.test(nextText)) {
            break;
        }
        text += ' ' + nextText.replace(/^\/\/\s*/, '');
    }
    return text;
}

// Citations that share one trailing clause (`File.cs:583 / :584`, `146 / 153 / 160`) form a group.
function groupCitations(citations, clauseText, testLine) {
    const groups = [];
    let gStart = 0;
    while (gStart < citations.length) {
        let gEnd = gStart;
        while (gEnd + 1 < citations.length) {
            const between = clauseText.slice(citations[gEnd].endOffset, citations[gEnd + 1].offset);
            const maskedBetween = maskBackticks(between);
            const separated = /`/.test(between) || /;/.test(maskedBetween) ||
                /\b(?:removed|replaced|mutated)\b/.test(maskedBetween);
            if (separated) {
                break;
            }
            gEnd++;
        }
        const clauseStart = citations[gEnd].endOffset;
        const clauseEnd = gEnd + 1 < citations.length ? citations[gEnd + 1].offset : clauseText.length;
        groups.push({
            testLine,
            clause: clauseText.slice(clauseStart, clauseEnd),
            targets: citations.slice(gStart, gEnd + 1),
        });
        gStart = gEnd + 1;
    }
    return groups;
}

function parseCitationsInFile(testFile) {
    const context = { file: null };
    const groups = [];
    for (const block of extractBlocks(readLines(testFile))) {
        for (let idx = 0; idx < block.length; idx++) {
            const { lineNo, text } = block[idx];
            const masked = maskBackticks(text);
            const bareFile = masked.match(/([A-Za-z0-9_./\\]+\.cs)\b/);
            if (bareFile && !/[A-Za-z0-9_./\\]+\.cs:\d+/.test(masked)) {
                context.file = bareFile[1];
            }
            const citations = parseLineCitations(masked, context);
            if (citations.length > 0) {
                groups.push(...groupCitations(citations, collectClauseText(block, idx), lineNo));
            }
        }
    }
    return groups;
}

// ---------------------------------------------------------------------------------------------
// Matching quotes against the cited code

// The quotes of the original code: a quote after `->` or `mutated` is the mutated code and is skipped,
// unless another `->` follows it. A quote right after `after` is marked as context.
function extractOriginalQuotes(clause) {
    const quotes = [];
    const tokenScan = /->|\bmutated\b|`([^`]+)`/g;
    let inMutatedRhs = false;
    let m;
    while ((m = tokenScan.exec(clause)) !== null) {
        if (m[0] === '->' || m[0] === 'mutated') {
            inMutatedRhs = true;
        } else if (m[1] !== undefined) {
            const afterQuote = clause.slice(tokenScan.lastIndex);
            if (!inMutatedRhs || /^\s*->/.test(afterQuote)) {
                const isContext = /\bafter\s*$/.test(clause.slice(0, m.index));
                quotes.push({ text: m[1].trim(), isContext });
                inMutatedRhs = false;
            }
        }
    }
    return quotes;
}

function normalizeWs(s) {
    return s.replace(/\s+/g, ' ').trim();
}

// `...` in the quote matches anything; a trailing `;` may be left out.
function matchWildcardParts(haystack, quote) {
    const parts = quote.split('...').map(normalizeWs).filter(Boolean);
    let pos = 0;
    for (let i = 0; i < parts.length; i++) {
        let part = parts[i];
        if (i === parts.length - 1 && part.endsWith(';') && !haystack.includes(part, pos)) {
            part = part.slice(0, -1).trim();
        }
        const idx = haystack.indexOf(part, pos);
        if (idx === -1) {
            return false;
        }
        pos = idx + part.length;
    }
    return true;
}

// Removes receivers (`Type.Member` -> `Member`) and numeric casts from the cited code only.
function stripQualifiersAndCasts(s) {
    return normalizeWs(
        s
            .replace(/\(\s*(?:long|ulong|int|uint|short|ushort|byte|sbyte|double|float)\s*\)\s*/g, '')
            .replace(/\b(?:[A-Za-z_][A-Za-z0-9_]*\??\.)+([A-Za-z_][A-Za-z0-9_]*)/g, '$1')
    );
}

// Returns 'exact', 'qualifier', 'helper' or 'shift' for a match, null for a mismatch.
function matchQuote(citedText, quote, clause) {
    const code = normalizeWs(citedText);
    if (matchWildcardParts(code, quote)) {
        return 'exact';
    }
    if (matchWildcardParts(stripQualifiersAndCasts(code), quote)) {
        return 'qualifier';
    }
    const helperHint = maskBackticks(clause).match(/\(([A-Za-z_][A-Za-z0-9_.]*)\s*\(/);
    if (helperHint && new RegExp(`\\b${helperHint[1].split('.').pop()}\\s*[<(]`).test(code)) {
        return 'helper';
    }
    const shift = quote.match(/^(>>|<<)\s*([A-Za-z0-9_]+)$/);
    if (shift && new RegExp(`\\bShift[A-Za-z0-9_]*\\s*\\(.*\\b${shift[2]}\\b`).test(code)) {
        return 'shift';
    }
    return null;
}

// ---------------------------------------------------------------------------------------------
// Checking

function isNonCode(text) {
    return text.length === 0 || text.startsWith('//') || text === '}';
}

// Resolves a citation to its product file and cited text, or returns { error }.
function resolveCitation(products, repoRoot, citation) {
    if (!citation.fileSpec) {
        return { error: `citation :${citation.startLine} has no resolved file context` };
    }
    const full = products.resolve(citation.fileSpec);
    if (!full) {
        return { error: `cannot resolve product file '${citation.fileSpec}'` };
    }
    const prodRel = path.relative(repoRoot, full).replace(/\\/g, '/');
    const lines = products.lines(full);
    const { startLine, endLine } = citation;
    if (startLine < 1 || endLine < startLine || endLine > lines.length) {
        return { error: `${prodRel}:${startLine}-${endLine} out of range (1..${lines.length})` };
    }
    const join = (from, to) => lines.slice(from - 1, to).map(l => l.trim()).join(' ');
    const citedText = join(startLine, endLine);
    if (isNonCode(citedText)) {
        return { error: `${prodRel}:${startLine} points at non-code line ('${citedText}')` };
    }
    return { prodRel, startLine, citedText, contextText: join(Math.max(1, startLine - 1), endLine) };
}

const ProseChecks = [
    [/\bThrowIfNull\b/, /ThrowIfNull/],
    [/\bArray\.Copy\b/, /Array\.Copy/],
    [/\bPadTo\b/, /PadTo/],
    [/\bWriteLimbs\b/, /WriteLimbs/],
    [/\bmemset\b/i, /ClearMemory|Memset|Clear\(/],
    [/\bthrow\s+removed\b/i, /\bthrow\b|ThrowIf/],
];

// Checks one quote against the cited text of one or several targets; records the outcome.
function checkQuote(result, where, targets, quote, clause) {
    const text = targets.map(t => (quote.isContext ? t.contextText : t.citedText)).join(' ');
    const kind = matchQuote(text, quote.text, clause);
    const first = targets[0];
    const line = `${where} -> ${first.prodRel}:${first.startLine} \`${quote.text}\``;
    if (kind === null) {
        result.failures.push(`${where}: ${first.prodRel}:${first.startLine} quote \`${quote.text}\` ` +
            `not found at '${first.citedText}'`);
    } else if (kind === 'exact') {
        result.exact.push(line);
    } else {
        result.fuzzy.push(`${line} (${kind})`);
    }
}

function checkGroup(result, products, repoRoot, relTest, group) {
    const where = `${relTest}:${group.testLine}`;
    const targets = [];
    let resolvedAll = true;
    for (const citation of group.targets) {
        result.citations++;
        const resolved = resolveCitation(products, repoRoot, citation);
        if (resolved.error) {
            result.failures.push(`${where}: ${resolved.error}`);
            resolvedAll = false;
        } else {
            targets.push(resolved);
        }
    }
    if (!resolvedAll) {
        return;
    }

    const quotes = extractOriginalQuotes(group.clause);
    if (quotes.length === 0) {
        const combined = targets.map(t => t.citedText).join(' ');
        for (const [trigger, expected] of ProseChecks) {
            if (trigger.test(group.clause) && !expected.test(combined)) {
                result.failures.push(`${where}: ${targets[0].prodRel}:${targets[0].startLine} ` +
                    `prose '${trigger}' not matched at '${targets[0].citedText}'`);
            }
        }
        return;
    }

    result.quotedClauses++;
    // One quote over several targets (`146 / 153 / 160`) must be on each of them, unless the
    // clause says they are representative (`and the like`).
    const eachTarget = targets.length > 1 && quotes.length === 1 && !/\band the like\b/i.test(group.clause);
    for (const quote of quotes) {
        if (eachTarget) {
            for (const target of targets) {
                checkQuote(result, where, [target], quote, group.clause);
            }
        } else {
            checkQuote(result, where, targets, quote, group.clause);
        }
    }
}

function report(result, verbose) {
    if (verbose) {
        result.exact.forEach(l => console.log(`OK ${l}`));
    }
    result.fuzzy.forEach(l => console.log(`FUZZY ${l}`));
    result.failures.forEach(f => console.error(f));
    const counts = `${result.citations} citations (${result.quotedClauses} quoted clauses; ` +
        `${result.exact.length} exact and ${result.fuzzy.length} FUZZY quote matches)`;
    if (result.failures.length > 0) {
        console.error(`\nFAILED: ${result.failures.length} mismatch(es) across ${counts}.`);
        return 1;
    }
    console.log(`OK: 0 mismatches across ${counts}.`);
    return 0;
}

function main() {
    const { repoRoot, verbose } = parseArgs(process.argv.slice(2));
    const srcRoot = path.join(repoRoot, 'src', 'Stdlib', 'Kassyi.GSharp.Stdlib.GpuMath');
    const testRoot = path.join(repoRoot, 'tests', 'Stdlib', 'Kassyi.GSharp.Stdlib.GpuMath.Tests');
    if (!fs.existsSync(srcRoot) || !fs.existsSync(testRoot)) {
        console.error(`Error: expected GpuMath source and test directories under '${repoRoot}'.`);
        process.exit(2);
    }

    const products = createProductIndex(repoRoot, srcRoot);
    const result = { citations: 0, quotedClauses: 0, exact: [], fuzzy: [], failures: [] };
    for (const testFile of listCsFiles(testRoot).sort()) {
        const relTest = path.relative(repoRoot, testFile).replace(/\\/g, '/');
        for (const group of parseCitationsInFile(testFile)) {
            checkGroup(result, products, repoRoot, relTest, group);
        }
    }
    process.exit(report(result, verbose));
}

main();
