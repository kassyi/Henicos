#!/usr/bin/env node
// Compares BenchmarkDotNet JSON results between a baseline and a head run using the Mann-Whitney U test.
// Usage: node scripts/human/bench-compare.js --base <dir|file> --head <dir|file> [--threshold <percent>] [--pvalue <p>] [--json-out <file>]
//   Reads *-report-full.json and *-report-full-compressed.json under --base and --head, extracts Actual Workload
//   iteration samples (Nanoseconds / Operations), computes the two-sided Mann-Whitney U test (with tie and continuity
//   correction on the normal approximation, plus exact distribution for small untied samples), prints a summary table,
//   and exits with code 1 when any benchmark regresses by more than --threshold % (default 5%) with p < --pvalue (0.01).
'use strict';

const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');

const helpText = [
    'Usage: node scripts/human/bench-compare.js --base <dir|file> --head <dir|file> [options]',
    '',
    '  --base <dir|file>       Baseline BenchmarkDotNet JSON file or directory searched recursively.',
    '  --head <dir|file>       Head BenchmarkDotNet JSON file or directory searched recursively.',
    '  --threshold <percent>   Relative regression/improvement threshold in percent (default: 5).',
    '  --pvalue <p>            Two-sided Mann-Whitney U p-value threshold (default: 0.01).',
    '  --json-out <file>       Optional path to write structured JSON comparison results.',
    '  --help, -h              Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(message + '\n');
    process.exit(1);
}

function parseArgs(argv) {
    const options = {
        base: null,
        head: null,
        threshold: 5.0,
        pvalue: 0.01,
        jsonOut: null,
    };

    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--base' || arg === '--head' || arg === '--json-out') {
            const value = argv[++i];
            if (value === undefined || value.startsWith('--')) {
                fail(`${arg} requires a path value.\n\n${helpText}`);
            }
            const resolved = path.resolve(value);
            if (arg === '--base') {
                options.base = resolved;
            } else if (arg === '--head') {
                options.head = resolved;
            } else {
                options.jsonOut = resolved;
            }
        } else if (arg === '--threshold') {
            const value = argv[++i];
            const parsed = Number.parseFloat(value);
            if (value === undefined || Number.isNaN(parsed) || parsed < 0) {
                fail(`--threshold requires a non-negative number, got: ${value}\n\n${helpText}`);
            }
            options.threshold = parsed;
        } else if (arg === '--pvalue') {
            const value = argv[++i];
            const parsed = Number.parseFloat(value);
            if (value === undefined || Number.isNaN(parsed) || parsed <= 0 || parsed >= 1) {
                fail(`--pvalue requires a number in (0, 1), got: ${value}\n\n${helpText}`);
            }
            options.pvalue = parsed;
        } else {
            fail(`Unknown argument: ${arg}\n\n${helpText}`);
        }
    }

    if (!options.base || !options.head) {
        fail(`Both --base and --head are required.\n\n${helpText}`);
    }

    return options;
}

function findReportFiles(target, label) {
    if (!fs.existsSync(target)) {
        fail(`${label} path does not exist: ${target}`);
    }
    const stat = fs.statSync(target);
    if (stat.isFile()) {
        return [target];
    }

    const fullReports = [];
    const fallbackJsons = [];

    const walk = dir => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const fullPath = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(fullPath);
            } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.json')) {
                const lower = entry.name.toLowerCase();
                if (lower.endsWith('-report-full.json') || lower.endsWith('-report-full-compressed.json')) {
                    fullReports.push(fullPath);
                } else {
                    fallbackJsons.push(fullPath);
                }
            }
        }
    };

    walk(target);
    const selected = fullReports.length > 0 ? fullReports : fallbackJsons;
    selected.sort((a, b) => a.localeCompare(b));
    if (selected.length === 0) {
        fail(`No BenchmarkDotNet JSON report files found in ${label}: ${target}`);
    }
    return selected;
}

function benchmarkKey(bench) {
    const ns = bench.Namespace ? `${bench.Namespace}.` : '';
    const type = bench.Type || '';
    const method = bench.Method || bench.MethodTitle || '';
    const params = bench.Parameters ? `[${bench.Parameters}]` : '';
    if (type || method) {
        return `${ns}${type}.${method}${params}`;
    }
    return bench.FullName || 'UnknownBenchmark';
}

function displayName(bench) {
    const type = bench.Type || '';
    const method = bench.Method || bench.MethodTitle || '';
    const params = bench.Parameters ? `[${bench.Parameters}]` : '';
    if (type && method) {
        return `${type}.${method}${params}`;
    }
    return bench.FullName || benchmarkKey(bench);
}

function extractWorkloadSamples(bench) {
    if (Array.isArray(bench.Measurements) && bench.Measurements.length > 0) {
        const samples = [];
        for (const m of bench.Measurements) {
            if (m && m.IterationMode === 'Workload' && m.IterationStage === 'Actual') {
                const ns = Number(m.Nanoseconds);
                const ops = Number(m.Operations) > 0 ? Number(m.Operations) : 1;
                if (Number.isFinite(ns) && ns >= 0) {
                    samples.push(ns / ops);
                }
            }
        }
        if (samples.length > 0) {
            return samples;
        }
    }

    if (bench.Statistics && Array.isArray(bench.Statistics.OriginalValues) && bench.Statistics.OriginalValues.length > 0) {
        return bench.Statistics.OriginalValues.map(Number).filter(v => Number.isFinite(v));
    }

    return [];
}

function loadBenchmarks(files, label) {
    const map = new Map();
    for (const file of files) {
        let parsed;
        try {
            parsed = JSON.parse(fs.readFileSync(file, 'utf8'));
        } catch (err) {
            fail(`Failed to parse JSON in ${file}: ${err.message}`);
        }
        if (!parsed || !Array.isArray(parsed.Benchmarks)) {
            continue;
        }
        for (const bench of parsed.Benchmarks) {
            const key = benchmarkKey(bench);
            const samples = extractWorkloadSamples(bench);
            if (samples.length === 0) {
                fail(
                    `Benchmark '${key}' in ${file} (${label}) has no Actual Workload measurements. ` +
                    'Ensure JsonExporter.Full or JsonExporter.FullCompressed is enabled.'
                );
            }
            map.set(key, {
                key,
                name: displayName(bench),
                fullName: bench.FullName || key,
                samples,
                file: path.relative(repoRoot, file).split(path.sep).join('/'),
            });
        }
    }
    if (map.size === 0) {
        fail(`No valid Benchmarks entries found in ${label} files: ${files.join(', ')}`);
    }
    return map;
}

function computeSummaryStats(samples) {
    const n = samples.length;
    const sorted = [...samples].sort((a, b) => a - b);
    const mid = Math.floor(n / 2);
    const median = n % 2 === 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
    let sum = 0;
    for (let i = 0; i < n; i++) {
        sum += samples[i];
    }
    const mean = sum / n;
    let sqDiff = 0;
    for (let i = 0; i < n; i++) {
        const d = samples[i] - mean;
        sqDiff += d * d;
    }
    const stdDev = n > 1 ? Math.sqrt(sqDiff / (n - 1)) : 0;
    return {
        n,
        median,
        mean,
        stdDev,
        min: sorted[0],
        max: sorted[n - 1],
    };
}

// Abramowitz & Stegun 7.1.26 approximation for erfc(|z| / sqrt(2)), which equals the two-sided standard normal p-value.
function normalTwoSidedPValue(absZ) {
    if (!Number.isFinite(absZ) || absZ <= 0) {
        return 1.0;
    }
    const x = absZ / Math.SQRT2;
    const p = 0.3275911;
    const a1 = 0.254829592;
    const a2 = -0.284496736;
    const a3 = 1.421413741;
    const a4 = -1.453152027;
    const a5 = 1.061405429;
    const t = 1.0 / (1.0 + p * x);
    const poly = (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t;
    const erfc = poly * Math.exp(-x * x);
    return Math.min(1.0, Math.max(0.0, erfc));
}

// Exact two-sided Mann-Whitney U p-value via dynamic programming when there are no ties and n1 + n2 <= 60.
function exactMannWhitneyTwoSidedPValue(n1, n2, uMin) {
    const maxU = n1 * n2;
    const kMax = Math.floor(uMin + 1e-9);
    // dp[i][u] = number of ways to form U = u with a sample of size i against a sample of size j
    let dp = Array.from({ length: n1 + 1 }, () => new Float64Array(maxU + 1));
    dp[0][0] = 1;

    for (let step = 1; step <= n1 + n2; step++) {
        const next = Array.from({ length: n1 + 1 }, () => new Float64Array(maxU + 1));
        for (let i = 0; i <= n1; i++) {
            const j = step - 1 - i;
            if (j < 0 || j > n2) {
                continue;
            }
            const row = dp[i];
            // Option 1: next element comes from group 2 (adds nothing to U_1)
            if (j + 1 <= n2) {
                const target = next[i];
                for (let u = 0; u <= maxU; u++) {
                    target[u] += row[u];
                }
            }
            // Option 2: next element comes from group 1 (exceeds all j elements of group 2 seen so far, adding j to U_1)
            if (i + 1 <= n1) {
                const target = next[i + 1];
                for (let u = 0; u + j <= maxU; u++) {
                    target[u + j] += row[u];
                }
            }
        }
        dp = next;
    }

    const dist = dp[n1];
    let total = 0;
    let tail = 0;
    for (let u = 0; u <= maxU; u++) {
        total += dist[u];
        if (u <= kMax) {
            tail += dist[u];
        }
    }
    if (total <= 0) {
        return 1.0;
    }
    return Math.min(1.0, (2.0 * tail) / total);
}

/**
 * Computes the Mann-Whitney U test between baseSamples (x) and headSamples (y), matching Perfolizer / BenchmarkDotNet:
 * ranks the combined sample with average ranks for ties, computes U_x, U_y, tie-corrected variance, continuity-corrected
 * z-score, and two-sided p-value.
 */
function mannWhitneyUTest(baseSamples, headSamples) {
    const n1 = baseSamples.length;
    const n2 = headSamples.length;
    const N = n1 + n2;

    const combined = [];
    for (let i = 0; i < n1; i++) {
        combined.push({ value: baseSamples[i], group: 0 });
    }
    for (let j = 0; j < n2; j++) {
        combined.push({ value: headSamples[j], group: 1 });
    }
    combined.sort((a, b) => a.value - b.value);

    let rankSumBase = 0;
    let tieCorrectionSum = 0;
    let hasTies = false;

    let i = 0;
    while (i < N) {
        let j = i + 1;
        while (j < N && combined[j].value === combined[i].value) {
            j++;
        }
        const tieCount = j - i;
        // 1-based average rank for indices i .. j-1
        const avgRank = 0.5 * (i + 1 + j);
        for (let k = i; k < j; k++) {
            if (combined[k].group === 0) {
                rankSumBase += avgRank;
            }
        }
        if (tieCount > 1) {
            hasTies = true;
            tieCorrectionSum += tieCount * tieCount * tieCount - tieCount;
        }
        i = j;
    }

    // U1 counts how many times a base sample exceeds a head sample; U2 counts how many times a head sample exceeds a base sample.
    const uBase = rankSumBase - (n1 * (n1 + 1)) / 2.0;
    const uHead = n1 * n2 - uBase;
    const uMin = Math.min(uBase, uHead);

    const mu = (n1 * n2) / 2.0;
    const tieTerm = N > 1 ? tieCorrectionSum / (N * (N - 1)) : 0;
    const variance = ((n1 * n2) / 12.0) * (N + 1 - tieTerm);

    if (variance <= 0) {
        return { u: uMin, uBase, uHead, z: 0, pValue: 1.0 };
    }

    const sigma = Math.sqrt(variance);
    const diff = Math.abs(uBase - mu);
    const correctedDiff = Math.max(0.0, diff - 0.5);
    const z = (uHead >= uBase ? 1 : -1) * (correctedDiff / sigma);
    const normalP = normalTwoSidedPValue(Math.abs(z));

    const pValue = !hasTies && N <= 60
        ? exactMannWhitneyTwoSidedPValue(n1, n2, uMin)
        : normalP;

    return { u: uMin, uBase, uHead, z, pValue };
}

function formatDurationNs(ns) {
    const abs = Math.abs(ns);
    if (abs >= 1e9) {
        return `${(ns / 1e9).toFixed(3)} s`;
    }
    if (abs >= 1e6) {
        return `${(ns / 1e6).toFixed(2)} ms`;
    }
    if (abs >= 1e3) {
        return `${(ns / 1e3).toFixed(2)} us`;
    }
    return `${ns.toFixed(2)} ns`;
}

function formatPValue(p) {
    if (p < 0.0001) {
        return '<0.0001';
    }
    return p.toFixed(4);
}

function pad(str, width, rightAlign = false) {
    const s = String(str);
    if (s.length >= width) {
        return s;
    }
    const spaces = ' '.repeat(width - s.length);
    return rightAlign ? spaces + s : s + spaces;
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    const baseFiles = findReportFiles(options.base, '--base');
    const headFiles = findReportFiles(options.head, '--head');

    const baseMap = loadBenchmarks(baseFiles, 'base');
    const headMap = loadBenchmarks(headFiles, 'head');

    const commonKeys = [...baseMap.keys()].filter(k => headMap.has(k)).sort((a, b) => a.localeCompare(b));
    if (commonKeys.length === 0) {
        fail('No matching benchmarks found between --base and --head.');
    }

    const comparisons = [];
    let regressions = 0;
    let improvements = 0;
    let noChanges = 0;

    for (const key of commonKeys) {
        const baseBench = baseMap.get(key);
        const headBench = headMap.get(key);

        const baseStats = computeSummaryStats(baseBench.samples);
        const headStats = computeSummaryStats(headBench.samples);
        const mw = mannWhitneyUTest(baseBench.samples, headBench.samples);

        const medianDeltaPct = baseStats.median > 0
            ? ((headStats.median - baseStats.median) / baseStats.median) * 100.0
            : 0.0;
        const meanDeltaPct = baseStats.mean > 0
            ? ((headStats.mean - baseStats.mean) / baseStats.mean) * 100.0
            : 0.0;

        let status = 'NO_CHANGE';
        if (mw.pValue < options.pvalue) {
            if (medianDeltaPct > options.threshold) {
                status = 'REGRESSION';
                regressions++;
            } else if (medianDeltaPct < -options.threshold) {
                status = 'IMPROVEMENT';
                improvements++;
            } else {
                noChanges++;
            }
        } else {
            noChanges++;
        }

        comparisons.push({
            key,
            name: baseBench.name,
            base: baseStats,
            head: headStats,
            medianDeltaPercent: medianDeltaPct,
            meanDeltaPercent: meanDeltaPct,
            mannWhitneyU: mw.u,
            uBase: mw.uBase,
            uHead: mw.uHead,
            zScore: mw.z,
            pValue: mw.pValue,
            status,
        });
    }

    const rows = comparisons.map(c => ({
        name: c.name,
        n: `${c.base.n}/${c.head.n}`,
        baseMedian: formatDurationNs(c.base.median),
        headMedian: formatDurationNs(c.head.median),
        delta: `${c.medianDeltaPercent >= 0 ? '+' : ''}${c.medianDeltaPercent.toFixed(2)}%`,
        u: c.mannWhitneyU.toFixed(1),
        p: formatPValue(c.pValue),
        status: c.status,
    }));

    const headers = {
        name: 'Benchmark',
        n: 'N (B/H)',
        baseMedian: 'Base Median',
        headMedian: 'Head Median',
        delta: 'Delta (%)',
        u: 'U',
        p: 'p-value',
        status: 'Status',
    };

    const widths = {};
    for (const col of Object.keys(headers)) {
        widths[col] = Math.max(headers[col].length, ...rows.map(r => r[col].length));
    }

    process.stdout.write(
        `Mann-Whitney U Benchmark Comparison (threshold: >${options.threshold}% change, p < ${options.pvalue})\n\n`
    );
    process.stdout.write(
        `| ${pad(headers.name, widths.name)} | ${pad(headers.n, widths.n, true)} | ` +
        `${pad(headers.baseMedian, widths.baseMedian, true)} | ${pad(headers.headMedian, widths.headMedian, true)} | ` +
        `${pad(headers.delta, widths.delta, true)} | ${pad(headers.u, widths.u, true)} | ` +
        `${pad(headers.p, widths.p, true)} | ${pad(headers.status, widths.status)} |\n`
    );
    process.stdout.write(
        `|-${'-'.repeat(widths.name)}-|-${'-'.repeat(widths.n)}:|` +
        `-${'-'.repeat(widths.baseMedian)}:|-${'-'.repeat(widths.headMedian)}:|` +
        `-${'-'.repeat(widths.delta)}:|-${'-'.repeat(widths.u)}:|` +
        `-${'-'.repeat(widths.p)}:|-${'-'.repeat(widths.status)}-|\n`
    );

    for (const r of rows) {
        process.stdout.write(
            `| ${pad(r.name, widths.name)} | ${pad(r.n, widths.n, true)} | ` +
            `${pad(r.baseMedian, widths.baseMedian, true)} | ${pad(r.headMedian, widths.headMedian, true)} | ` +
            `${pad(r.delta, widths.delta, true)} | ${pad(r.u, widths.u, true)} | ` +
            `${pad(r.p, widths.p, true)} | ${pad(r.status, widths.status)} |\n`
        );
    }

    process.stdout.write(
        `\nSummary: ${comparisons.length} compared, ${regressions} regression(s), ` +
        `${improvements} improvement(s), ${noChanges} unchanged.\n`
    );

    if (options.jsonOut) {
        const outDir = path.dirname(options.jsonOut);
        if (!fs.existsSync(outDir)) {
            fs.mkdirSync(outDir, { recursive: true });
        }
        const payload = {
            thresholdPercent: options.threshold,
            pValueThreshold: options.pvalue,
            summary: {
                compared: comparisons.length,
                regressions,
                improvements,
                unchanged: noChanges,
            },
            comparisons,
        };
        fs.writeFileSync(options.jsonOut, JSON.stringify(payload, null, 2) + '\n', 'utf8');
    }

    if (regressions > 0) {
        process.exit(1);
    }
}

if (require.main === module) {
    main();
} else {
    module.exports = {
        mannWhitneyUTest,
        exactMannWhitneyTwoSidedPValue,
        normalTwoSidedPValue,
        computeSummaryStats,
    };
}
