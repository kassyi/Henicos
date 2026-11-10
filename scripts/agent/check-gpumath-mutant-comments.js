#!/usr/bin/env node
// Verifies that source line citations (File.cs:N, :N, LN, / N, line N) in GpuMath test comments
// resolve to existing product files and match the quoted code or statement at the cited lines.
// Usage (from anywhere in the repository):
//   node scripts/agent/check-gpumath-mutant-comments.js [--root <repo-root>] [--verbose]
'use strict';

const fs = require('fs');
const path = require('path');

function printHelp() {
    console.log([
        'Usage: node scripts/agent/check-gpumath-mutant-comments.js [--root <dir>] [--verbose] [--help]',
        '',
        'Options:',
        '  --root <dir>  Repository root to check (default: resolved from script path)',
        '  --verbose     Print matched citations as well as mismatches',
        '  --help        Show this help message',
    ].join('\n'));
}

let repoRoot = path.resolve(__dirname, '..', '..');
let verbose = false;

const args = process.argv.slice(2);
for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === '--help' || arg === '-h') {
        printHelp();
        process.exit(0);
    } else if (arg === '--verbose') {
        verbose = true;
    } else if (arg === '--root') {
        if (i + 1 >= args.length) {
            console.error('Error: --root requires a directory argument.');
            process.exit(2);
        }
        repoRoot = path.resolve(args[++i]);
    } else {
        console.error(`Error: unknown option '${arg}'. Run with --help for usage.`);
        process.exit(2);
    }
}

const srcRoot = path.join(repoRoot, 'src', 'Stdlib', 'Kassyi.GSharp.Stdlib.GpuMath');
const testRoot = path.join(repoRoot, 'tests', 'Stdlib', 'Kassyi.GSharp.Stdlib.GpuMath.Tests');

if (!fs.existsSync(srcRoot) || !fs.existsSync(testRoot)) {
    console.error(`Error: expected GpuMath source and test directories under '${repoRoot}'.`);
    process.exit(2);
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

const prodFiles = listCsFiles(srcRoot);
const prodLinesCache = new Map();

function resolveProdFile(spec) {
    const normalized = spec.replace(/\\/g, '/');
    if (normalized.startsWith('src/')) {
        const full = path.join(repoRoot, normalized);
        return fs.existsSync(full) ? full : null;
    }
    const matches = prodFiles.filter(f => {
        const rel = path.relative(srcRoot, f).replace(/\\/g, '/');
        return rel === normalized || path.basename(f) === normalized || rel.endsWith('/' + normalized);
    });
    return matches.length === 1 ? matches[0] : null;
}

function getProdLines(fullPath) {
    if (!prodLinesCache.has(fullPath)) {
        const content = fs.readFileSync(fullPath, 'utf8').replace(/^\uFEFF/, '');
        prodLinesCache.set(fullPath, content.split(/\r?\n/));
    }
    return prodLinesCache.get(fullPath);
}

function maskBackticks(text) {
    return text.replace(/`[^`]*`/g, m => ' '.repeat(m.length));
}

function extractBlocks(testFile) {
    const lines = fs.readFileSync(testFile, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/);
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

const TOKEN_PATTERN = [
    '([A-Za-z0-9_./\\\\]+\\.cs):(\\d+)(?:-(\\d+))?',
    '(?<=[\\s,;(/:]|^):(\\d+)(?:-(\\d+))?',
    '\\bL(\\d+)(?:-(\\d+))?\\b',
    '\\bline\\s+(\\d+)(?:-(\\d+))?\\b',
].join('|');

const CONT_PATTERN = new RegExp(
    '^(\\s*\\/\\s*|\\s*\\([^)]*\\)\\s*,\\s*)(\\d+)(?:-(\\d+))?' +
    '(?!\\s*(?:words|bits|limbs|blocks|threads|iterations|ul|u\\b|L\\b))'
);

function parseCitationsInFile(testFile) {
    const blocks = extractBlocks(testFile);
    const groups = [];
    let fileContext = null;

    for (const block of blocks) {
        for (let idx = 0; idx < block.length; idx++) {
            const { lineNo, text } = block[idx];
            const masked = maskBackticks(text);

            const bareFileMatch = masked.match(/([A-Za-z0-9_./\\]+\.cs)\b/);
            if (bareFileMatch && !/[A-Za-z0-9_./\\]+\.cs:\d+/.test(masked)) {
                fileContext = bareFileMatch[1];
            }

            const tokenRe = new RegExp(TOKEN_PATTERN, 'g');
            const lineCitations = [];
            let m;
            while ((m = tokenRe.exec(masked)) !== null) {
                let startLine;
                let endLine;
                if (m[1] !== undefined) {
                    fileContext = m[1];
                    startLine = Number(m[2]);
                    endLine = m[3] ? Number(m[3]) : startLine;
                } else if (m[4] !== undefined) {
                    startLine = Number(m[4]);
                    endLine = m[5] ? Number(m[5]) : startLine;
                } else if (m[6] !== undefined) {
                    startLine = Number(m[6]);
                    endLine = m[7] ? Number(m[7]) : startLine;
                } else if (m[8] !== undefined) {
                    startLine = Number(m[8]);
                    endLine = m[9] ? Number(m[9]) : startLine;
                }

                const citeEndOffset = m.index + m[0].length;
                lineCitations.push({
                    fileSpec: fileContext,
                    startLine,
                    endLine,
                    offset: m.index,
                    endOffset: citeEndOffset,
                });

                let pos = tokenRe.lastIndex;
                while (true) {
                    const rest = masked.slice(pos);
                    const cont = rest.match(CONT_PATTERN);
                    if (!cont) {
                        break;
                    }
                    const cStart = Number(cont[2]);
                    const cEnd = cont[3] ? Number(cont[3]) : cStart;
                    const nextPos = pos + cont[0].length;
                    lineCitations.push({
                        fileSpec: fileContext,
                        startLine: cStart,
                        endLine: cEnd,
                        offset: pos + cont[1].length,
                        endOffset: nextPos,
                    });
                    pos = nextPos;
                    tokenRe.lastIndex = pos;
                }
            }

            if (lineCitations.length === 0) {
                continue;
            }

            let combinedText = text;
            let look = idx + 1;
            while (look < block.length) {
                const nextText = block[look].text;
                const nextMasked = maskBackticks(nextText);
                if (/\/\/\s*(?:mutant|equivalent):/.test(nextText) ||
                    /[A-Za-z0-9_./\\]+\.cs:\d+/.test(nextMasked) ||
                    /\bline\s+\d+\b/.test(nextMasked) ||
                    /^\/\/\s*-\s/.test(nextText)) {
                    break;
                }
                combinedText += ' ' + nextText.replace(/^\/\/\s*/, '');
                look++;
            }

            // Group citations sharing a trailing clause (`File.cs:583 / :584` or `146 / 153 / 160 / 167`).
            let gStart = 0;
            while (gStart < lineCitations.length) {
                let gEnd = gStart;
                while (gEnd + 1 < lineCitations.length) {
                    const from = lineCitations[gEnd].endOffset;
                    const to = lineCitations[gEnd + 1].offset;
                    const rawBetween = combinedText.slice(from, to);
                    const maskedBetween = maskBackticks(rawBetween);
                    const hasStopWord = /\b(?:removed|replaced|mutated)\b/.test(maskedBetween);
                    if (!/`/.test(rawBetween) && !hasStopWord && !/;/.test(maskedBetween)) {
                        gEnd++;
                    } else {
                        break;
                    }
                }
                const clauseStart = lineCitations[gEnd].endOffset;
                const hasNext = gEnd + 1 < lineCitations.length;
                const clauseEnd = hasNext ? lineCitations[gEnd + 1].offset : combinedText.length;
                groups.push({
                    testLine: lineNo,
                    clause: combinedText.slice(clauseStart, clauseEnd),
                    targets: lineCitations.slice(gStart, gEnd + 1),
                });
                gStart = gEnd + 1;
            }
        }
    }

    return groups;
}

// Extract original-code backtick quotes (excluding mutated RHS quotes after `->` or `mutated`).
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
                quotes.push(m[1].trim());
                inMutatedRhs = false;
            }
        }
    }
    return quotes;
}

function normalizeWs(s) {
    return s.replace(/\s+/g, ' ').trim();
}

// Strip receiver qualifiers (`obj.Member` -> `Member`) and simple C# casts (`(long)x` -> `x`).
function stripQualifiersAndCasts(s) {
    return normalizeWs(
        s
            .replace(/\(\s*(?:long|ulong|int|uint|short|ushort|byte|sbyte|double|float)\s*\)\s*/g, '')
            .replace(/\b(?:[A-Za-z_][A-Za-z0-9_]*\??\.)+([A-Za-z_][A-Za-z0-9_]*)/g, '$1')
    );
}

const CSHARP_KEYWORDS = new Set([
    'if', 'else', 'for', 'while', 'return', 'var', 'new',
    'ref', 'out', 'in', 'true', 'false', 'null', 'this', 'is', 'not',
]);

function matchWildcardParts(haystack, quote) {
    const parts = quote.split('...').map(p => normalizeWs(p)).filter(Boolean);
    if (parts.length === 0) {
        return true;
    }
    let pos = 0;
    for (let i = 0; i < parts.length; i++) {
        let part = parts[i];
        if (i === parts.length - 1 && part.endsWith(';') && !haystack.includes(part)) {
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

function matchesQuoteInWindow(windowText, quote, clause) {
    const normWin = normalizeWs(windowText);

    // 1. Direct substring / `...` wildcard match
    if (matchWildcardParts(normWin, quote)) {
        return true;
    }

    // 2. Shorthand quote omitting receiver prefixes or numeric casts (`TryWriteBytes(...)`, `NumWords + 1`)
    const strippedWin = stripQualifiersAndCasts(normWin);
    const strippedQuote = stripQualifiersAndCasts(quote);
    if (matchWildcardParts(strippedWin, strippedQuote)) {
        return true;
    }

    // 3. Explicit helper call named in parentheses right after a shorthand quote (`ax -= a` (DeviceAddSub...))
    const helperHint = clause.match(/\(([A-Za-z_][A-Za-z0-9_.]*)\s*\(/);
    if (helperHint) {
        const helperShort = helperHint[1].split('.').pop();
        if (normWin.includes(helperShort)) {
            return true;
        }
    }

    // 4. Shift shorthand (`>> 1`, `<< leadingZeros`) lowered through a `Shift*` helper on the cited line
    const shiftMatch = quote.match(/^(>>|<<)\s*([A-Za-z0-9_]+)$/);
    if (shiftMatch && /\bShift[A-Za-z0-9_]*\s*\(/.test(normWin)) {
        const rhs = shiftMatch[2];
        if (normWin.includes(rhs)) {
            return true;
        }
    }

    // 5. Structural identifier + operator check when a single local/parameter was renamed or wrapped.
    const rawIdents = strippedQuote.match(/\b[A-Za-z_][A-Za-z0-9_]*\b/g) || [];
    const idents = rawIdents.filter(id => !CSHARP_KEYWORDS.has(id));
    const ops = strippedQuote.match(/==|!=|<=|>=|&&|\|\||<<|>>|[+\-*/<>]/g) || [];

    if (idents.length >= 2) {
        let matchedIdents = 0;
        let searchPos = 0;
        for (const id of idents) {
            const idx = strippedWin.indexOf(id, searchPos);
            if (idx !== -1) {
                matchedIdents++;
                searchPos = idx + id.length;
            }
        }
        const requiredIdents = Math.max(1, Math.ceil(idents.length * 0.5));
        const hasRewriteOps = ops.includes('+') && strippedWin.includes('-') &&
            idents.every(id => strippedWin.includes(id));
        const opsOk = ops.length === 0 || ops.some(op => strippedWin.includes(op)) || hasRewriteOps;

        if (matchedIdents >= requiredIdents && matchedIdents >= idents.length - 1 && opsOk) {
            return true;
        }
    }

    return false;
}

function formatQuoteMiss(relTest, testLine, win, quote) {
    return `${relTest}:${testLine}: ${win.prodRel}:${win.startLine} ` +
        `quote \`${quote}\` not found at '${win.primaryText}'`;
}

const testFiles = listCsFiles(testRoot).sort();
let totalCitations = 0;
let checkedGroupsWithQuotes = 0;
const failures = [];

for (const tf of testFiles) {
    const relTest = path.relative(repoRoot, tf).replace(/\\/g, '/');
    const groups = parseCitationsInFile(tf);

    for (const group of groups) {
        const groupWindows = [];
        let groupValid = true;
        const hasAfterContext = /\bafter\s+`/.test(group.clause);

        for (const c of group.targets) {
            totalCitations++;
            if (!c.fileSpec) {
                failures.push(
                    `${relTest}:${group.testLine}: citation :${c.startLine} has no resolved file context`
                );
                groupValid = false;
                continue;
            }
            const prodFull = resolveProdFile(c.fileSpec);
            if (!prodFull) {
                failures.push(`${relTest}:${group.testLine}: cannot resolve product file '${c.fileSpec}'`);
                groupValid = false;
                continue;
            }
            const prodRel = path.relative(repoRoot, prodFull).replace(/\\/g, '/');
            const pLines = getProdLines(prodFull);
            if (c.startLine < 1 || c.endLine < c.startLine || c.endLine > pLines.length) {
                const range = `${c.startLine}-${c.endLine}`;
                failures.push(
                    `${relTest}:${group.testLine}: ${prodRel}:${range} out of range (1..${pLines.length})`
                );
                groupValid = false;
                continue;
            }

            const primaryText = pLines.slice(c.startLine - 1, c.endLine).map(l => l.trim()).join(' ');
            if (primaryText.length === 0 || primaryText.startsWith('//') || primaryText === '}') {
                failures.push(
                    `${relTest}:${group.testLine}: ${prodRel}:${c.startLine} ` +
                    `points at non-code line ('${primaryText}')`
                );
                groupValid = false;
                continue;
            }

            const winStart = Math.max(0, c.startLine - (hasAfterContext ? 2 : 1));
            const winEnd = Math.min(pLines.length, c.endLine + 2);
            const windowText = pLines.slice(winStart, winEnd).map(l => l.trim()).join(' ');
            groupWindows.push({ prodRel, startLine: c.startLine, primaryText, windowText });
        }

        if (!groupValid || groupWindows.length === 0) {
            continue;
        }

        const combinedGroupWindow = groupWindows.map(w => w.windowText).join(' ');
        const isRepresentativeList = /\band the like\b/i.test(group.clause);
        const quotes = extractOriginalQuotes(group.clause);

        if (quotes.length > 0) {
            checkedGroupsWithQuotes++;
            for (const q of quotes) {
                if (groupWindows.length === 1 || isRepresentativeList) {
                    if (!matchesQuoteInWindow(combinedGroupWindow, q, group.clause)) {
                        failures.push(formatQuoteMiss(relTest, group.testLine, groupWindows[0], q));
                    } else if (verbose) {
                        const first = groupWindows[0];
                        console.log(`OK ${relTest}:${group.testLine} -> ${first.prodRel}:${first.startLine} \`${q}\``);
                    }
                } else if (quotes.length === 1) {
                    for (const w of groupWindows) {
                        if (!matchesQuoteInWindow(w.windowText, q, group.clause)) {
                            failures.push(formatQuoteMiss(relTest, group.testLine, w, q));
                        }
                    }
                } else if (!matchesQuoteInWindow(combinedGroupWindow, q, group.clause)) {
                    failures.push(formatQuoteMiss(relTest, group.testLine, groupWindows[0], q));
                }
            }
        } else {
            const proseChecks = [
                [/\bThrowIfNull\b/, /ThrowIfNull/],
                [/\bArray\.Copy\b/, /Array\.Copy/],
                [/\bPadTo\b/, /PadTo/],
                [/\bWriteLimbs\b/, /WriteLimbs/],
                [/\bmemset\b/i, /ClearMemory|Memset|Clear\(/],
                [/\bthrow\s+removed\b/i, /\bthrow\b|ThrowIf/],
            ];
            for (const [trigger, expected] of proseChecks) {
                if (trigger.test(group.clause) && !expected.test(combinedGroupWindow)) {
                    const first = groupWindows[0];
                    failures.push(
                        `${relTest}:${group.testLine}: ${first.prodRel}:${first.startLine} ` +
                        `prose '${trigger}' not matched at '${first.primaryText}'`
                    );
                }
            }
        }
    }
}

if (failures.length > 0) {
    for (const f of failures) {
        console.error(f);
    }
    console.error(
        `\nFAILED: ${failures.length} mismatch(es) across ${totalCitations} citations ` +
        `(${checkedGroupsWithQuotes} quoted clauses).`
    );
    process.exit(1);
}

console.log(
    `OK: 0 mismatches across ${totalCitations} citations (${checkedGroupsWithQuotes} quoted clauses).`
);
