#!/usr/bin/env node
// Builds and times Kassyi.GSharp.Tests.Cpu across barrier-model configurations (default, stack, managed-stack,
// both-stack), or runs the BarrierModelBenchmark micro-harness (--kernels), and writes a Markdown summary.
//
// Usage:
//   node scripts/human/time-barrier-models.js [--runs <n>] [--config <Debug|Release>] [--variants <csv>] [--out-dir <path>]
//   node scripts/human/time-barrier-models.js --kernels [--config <Debug|Release>] [--out-dir <path>]
//   node scripts/human/time-barrier-models.js --self-test
//   node scripts/human/time-barrier-models.js --help
'use strict';

const assert = require('assert');
const { spawnSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');
const cpuTestProject = path.join(
    'tests',
    'Compiler',
    'Kassyi.GSharp.Tests.Cpu',
    'Kassyi.GSharp.Tests.Cpu.csproj'
);

const VARIANT_PROPS = {
    default: [],
    stack: ['-p:GSharpNativeBarrierModel=Stack', '-p:GSharpOutputVariant=stack'],
    'managed-stack': ['-p:GSharpManagedBarrierModel=Stack', '-p:GSharpOutputVariant=managed-stack'],
    'both-stack': [
        '-p:GSharpNativeBarrierModel=Stack',
        '-p:GSharpManagedBarrierModel=Stack',
        '-p:GSharpOutputVariant=both-stack',
    ],
};
const DEFAULT_VARIANTS = ['default', 'stack', 'managed-stack', 'both-stack'];
const SUITE_FILTER = 'Category!=Stress&Category!=Benchmark';
const KERNEL_BENCH_FILTER = 'Category=Benchmark&FullyQualifiedName~BarrierModelBenchmark';

const helpText = [
    'Usage:',
    '  node scripts/human/time-barrier-models.js [--runs <n>] [--config <Debug|Release>] [--variants <csv>] [--out-dir <path>]',
    '  node scripts/human/time-barrier-models.js --kernels [--config <Debug|Release>] [--out-dir <path>]',
    '  node scripts/human/time-barrier-models.js --self-test',
    '',
    'Options:',
    '  --runs <n>          Number of interleaved runs per variant in suite mode. Default: 3.',
    '  --config <cfg>      Build configuration: Debug or Release. Default: Release.',
    '  --variants <csv>    Comma-separated variants: default,stack,managed-stack,both-stack.',
    '  --out-dir <path>    Output directory (default: tmp/time-barrier-models/<YYYY-MM-DD>).',
    '  --kernels           Build default and both-stack and run BarrierModelBenchmark into <out-dir>/bench.tsv,',
    '                      appending the kernel comparison table to <out-dir>/summary.md.',
    '  --self-test         Run built-in tests on synthetic .trx and bench.tsv data.',
    '  --help, -h          Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(`Error: ${message}\n`);
    process.exit(1);
}

function defaultOutDir() {
    const today = new Date().toISOString().slice(0, 10);
    return path.join(repoRoot, 'tmp', 'time-barrier-models', today);
}

function resolveOutDir(rawPath) {
    return path.isAbsolute(rawPath) ? rawPath : path.resolve(repoRoot, rawPath);
}

function normalizeConfig(raw) {
    const lower = raw.toLowerCase();
    if (lower === 'debug') {
        return 'Debug';
    }
    if (lower === 'release') {
        return 'Release';
    }
    fail(`Invalid --config '${raw}'. Expected Debug or Release.`);
}

function parseVariants(raw) {
    const parts = raw.split(',').map((s) => s.trim()).filter(Boolean);
    if (parts.length === 0) {
        fail('--variants requires at least one variant.');
    }
    const seen = new Set();
    const out = [];
    for (const v of parts) {
        if (!Object.prototype.hasOwnProperty.call(VARIANT_PROPS, v)) {
            fail(`Unknown variant '${v}'. Expected one of: ${DEFAULT_VARIANTS.join(', ')}.`);
        }
        if (!seen.has(v)) {
            seen.add(v);
            out.push(v);
        }
    }
    return out;
}

function parseArgs(argv) {
    const options = {
        mode: 'suite',
        runs: 3,
        config: 'Release',
        variants: [...DEFAULT_VARIANTS],
        outDir: defaultOutDir(),
    };

    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--self-test') {
            options.mode = 'self-test';
        } else if (arg === '--kernels') {
            options.mode = 'kernels';
        } else if (arg === '--runs' || arg === '--config' || arg === '--variants' || arg === '--out-dir') {
            const value = argv[++i];
            if (value === undefined) {
                fail(`${arg} requires a value.\n\n${helpText}`);
            }
            if (arg === '--runs') {
                options.runs = Number.parseInt(value, 10);
                if (!Number.isInteger(options.runs) || options.runs <= 0) {
                    fail(`--runs needs a positive integer, got '${value}'.`);
                }
            } else if (arg === '--config') {
                options.config = normalizeConfig(value);
            } else if (arg === '--variants') {
                options.variants = parseVariants(value);
            } else {
                options.outDir = resolveOutDir(value);
            }
        } else if (arg.startsWith('--runs=')) {
            const value = arg.slice('--runs='.length);
            options.runs = Number.parseInt(value, 10);
            if (!Number.isInteger(options.runs) || options.runs <= 0) {
                fail(`--runs needs a positive integer, got '${value}'.`);
            }
        } else if (arg.startsWith('--config=')) {
            options.config = normalizeConfig(arg.slice('--config='.length));
        } else if (arg.startsWith('--variants=')) {
            options.variants = parseVariants(arg.slice('--variants='.length));
        } else if (arg.startsWith('--out-dir=')) {
            options.outDir = resolveOutDir(arg.slice('--out-dir='.length));
        } else {
            fail(`Unknown argument: ${arg}\n\n${helpText}`);
        }
    }

    return options;
}

function getMsbuildArgs() {
    const queueArgs = (process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS || '')
        .split(/\s+/)
        .filter(Boolean);
    const nonWindows = process.platform === 'win32' ? [] : ['-p:EmbedUntrackedSources=false'];
    return [...queueArgs, ...nonWindows];
}

function runDotnet(args, extraEnv) {
    const env = {
        ...process.env,
        DiffEngine_Disabled: 'true',
        CI: 'true',
        GSHARP_TEST_NO_CUDA: '1',
        CUDA_VISIBLE_DEVICES: '-1',
        ...extraEnv,
    };
    const startNs = process.hrtime.bigint();
    const res = spawnSync('dotnet', args, { cwd: repoRoot, env, stdio: 'inherit' });
    const elapsedSeconds = Number(process.hrtime.bigint() - startNs) / 1e9;
    if (res.error) {
        return { exitCode: 1, wallSeconds: elapsedSeconds, error: res.error.message };
    }
    return { exitCode: res.status ?? 1, wallSeconds: elapsedSeconds, error: null };
}

function findTrxFiles(dir) {
    if (!fs.existsSync(dir)) {
        return [];
    }
    const found = [];
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            found.push(...findTrxFiles(full));
        } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.trx')) {
            found.push(full);
        }
    }
    return found.sort();
}

function decodeXml(text) {
    return text
        .replace(/&lt;/g, '<')
        .replace(/&gt;/g, '>')
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&#x([0-9a-fA-F]+);/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)))
        .replace(/&#([0-9]+);/g, (_, dec) => String.fromCodePoint(Number.parseInt(dec, 10)))
        .replace(/&amp;/g, '&');
}

function xmlAttributes(tagText) {
    const result = {};
    for (const match of tagText.matchAll(/([\w:]+)="([^"]*)"/g)) {
        result[match[1]] = decodeXml(match[2]);
    }
    return result;
}

// "hh:mm:ss.fffffff" -> seconds
function parseDuration(text) {
    const match = /^(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(text || '');
    return match ? Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3]) : 0;
}

function readTrxFile(file) {
    const text = fs.readFileSync(file, 'utf8');
    const definitions = new Map();
    for (const match of text.matchAll(/<UnitTest\s([^>]*)>([\s\S]*?)<\/UnitTest>/g)) {
        const unitTest = xmlAttributes(match[1]);
        const methodTag = /<TestMethod\s([^>]*?)\/?>/.exec(match[2]);
        const method = methodTag ? xmlAttributes(methodTag[1]) : {};
        definitions.set(unitTest.id, {
            assembly: path.win32.basename(unitTest.storage || method.codeBase || 'unknown'),
            className: method.className || '(unknown class)',
        });
    }

    const results = [];
    for (const match of text.matchAll(/<UnitTestResult\s([^>]*?)\/?>/g)) {
        const result = xmlAttributes(match[1]);
        const definition = definitions.get(result.testId) || { assembly: 'unknown', className: '(unknown class)' };
        results.push({
            name: result.testName || '(unnamed)',
            outcome: result.outcome || 'Unknown',
            seconds: parseDuration(result.duration),
            assembly: definition.assembly,
            className: definition.className,
        });
    }

    const timesTag = /<Times\s([^>]*?)\/?>/.exec(text);
    const times = timesTag ? xmlAttributes(timesTag[1]) : {};
    const start = Date.parse(times.start);
    const finish = Date.parse(times.finish);
    const xunitWallSeconds = Number.isNaN(start) || Number.isNaN(finish) ? 0 : (finish - start) / 1000;

    const summaryTag = /<ResultSummary\s([^>]*)>/.exec(text);
    const summaryAttrs = summaryTag ? xmlAttributes(summaryTag[1]) : {};
    const countersTag = /<Counters\s([^>]*?)\/?>/.exec(text);
    let counts;
    if (countersTag) {
        const c = xmlAttributes(countersTag[1]);
        const total = Number.parseInt(c.total || '0', 10) || results.length;
        const passed = Number.parseInt(c.passed || '0', 10) || 0;
        const failed = Number.parseInt(c.failed || '0', 10) || 0;
        const skipped = Number.parseInt(c.notExecuted || '0', 10) || Math.max(0, total - passed - failed);
        counts = { total, passed, failed, skipped };
    } else {
        let passed = 0;
        let failed = 0;
        let skipped = 0;
        for (const r of results) {
            if (r.outcome === 'Passed') {
                passed++;
            } else if (r.outcome === 'Failed') {
                failed++;
            } else {
                skipped++;
            }
        }
        counts = { total: results.length, passed, failed, skipped };
    }

    return {
        file,
        results,
        xunitWallSeconds,
        outcome: summaryAttrs.outcome || (counts.failed > 0 ? 'Failed' : 'Completed'),
        counts,
    };
}

function summarizeRunTrxFiles(trxFiles) {
    const classSums = new Map();
    const counts = { total: 0, passed: 0, failed: 0, skipped: 0 };
    let xunitWallSeconds = 0;

    for (const file of trxFiles) {
        const parsed = readTrxFile(file);
        xunitWallSeconds += parsed.xunitWallSeconds;
        counts.total += parsed.counts.total;
        counts.passed += parsed.counts.passed;
        counts.failed += parsed.counts.failed;
        counts.skipped += parsed.counts.skipped;
        for (const r of parsed.results) {
            classSums.set(r.className, (classSums.get(r.className) || 0) + r.seconds);
        }
    }

    return { classSums, counts, xunitWallSeconds };
}

function median(values) {
    if (values.length === 0) {
        return null;
    }
    const sorted = [...values].sort((a, b) => a - b);
    const mid = Math.floor(sorted.length / 2);
    return sorted.length % 2 === 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

function formatRatio(num, den) {
    if (num === null || den === null || den <= 0) {
        return 'n/a';
    }
    return `${(num / den).toFixed(2)}x`;
}

function formatSeconds(value, decimals = 2) {
    if (value === null || value === undefined || Number.isNaN(value)) {
        return 'n/a';
    }
    return value.toFixed(decimals);
}

function buildSuiteSummary({ config, runsCount, variants, buildRecords, runRecords }) {
    const lines = [
        `# Barrier Model Suite Timing (${config}, ${runsCount} interleaved run(s))`,
        '',
        `- Generated: ${new Date().toISOString()}`,
        `- Project: \`${cpuTestProject}\``,
        `- Filter: \`${SUITE_FILTER}\``,
        `- Machine note: check load conditions; re-run on an idle machine if comparing against release gates.`,
        '',
        '## Build Summary',
        '',
        '| Variant | Build | Wall (s) |',
        '| :--- | :--- | ---: |',
    ];

    for (const b of buildRecords) {
        lines.push(`| ${b.variant} | ${b.ok ? 'PASS' : `FAIL (exit ${b.exitCode})`} | ${formatSeconds(b.wallSeconds, 1)} |`);
    }

    lines.push('');
    lines.push('## Per-Run Results');
    lines.push('');
    lines.push('| Variant | Run | Status | Wall (s) | xUnit (s) | Passed | Failed | Skipped | Total |');
    lines.push('| :--- | ---: | :--- | ---: | ---: | ---: | ---: | ---: | ---: |');

    for (const r of runRecords) {
        const status = r.buildFailed
            ? 'SKIP (build failed)'
            : r.passed
                ? 'PASS'
                : `FAIL (exit ${r.exitCode})`;
        lines.push(
            `| ${r.variant} | ${r.run} | ${status} | ${formatSeconds(r.wallSeconds, 2)} | ${formatSeconds(r.xunitWallSeconds, 2)} | ${r.counts.passed} | ${r.counts.failed} | ${r.counts.skipped} | ${r.counts.total} |`
        );
    }

    const byVariant = new Map();
    for (const v of variants) {
        byVariant.set(v, runRecords.filter((r) => r.variant === v && !r.buildFailed));
    }

    const defaultRuns = byVariant.get('default') || [];
    const defaultMedianWall = median(defaultRuns.map((r) => r.wallSeconds));
    const defaultMedianXunit = median(defaultRuns.map((r) => r.xunitWallSeconds));

    lines.push('');
    lines.push('## Per-Variant Medians');
    lines.push('');
    lines.push('| Variant | Passed Runs | Median Wall (s) | Wall Ratio vs default | Median xUnit (s) | xUnit Ratio vs default |');
    lines.push('| :--- | :--- | ---: | ---: | ---: | ---: |');

    for (const v of variants) {
        const vRuns = byVariant.get(v) || [];
        const passCount = vRuns.filter((r) => r.passed).length;
        const medWall = median(vRuns.map((r) => r.wallSeconds));
        const medXunit = median(vRuns.map((r) => r.xunitWallSeconds));
        lines.push(
            `| ${v} | ${passCount}/${runsCount} | ${formatSeconds(medWall, 2)} | ${formatRatio(medWall, defaultMedianWall)} | ${formatSeconds(medXunit, 2)} | ${formatRatio(medXunit, defaultMedianXunit)} |`
        );
    }

    if (defaultRuns.length > 0) {
        const defaultClassMedians = computeClassMedians(defaultRuns);
        for (const v of variants) {
            if (v === 'default') {
                continue;
            }
            const vRuns = byVariant.get(v) || [];
            if (vRuns.length === 0) {
                continue;
            }
            const vClassMedians = computeClassMedians(vRuns);
            const deltas = [];
            for (const [className, vSec] of vClassMedians.entries()) {
                const dSec = defaultClassMedians.get(className) ?? 0;
                const delta = vSec - dSec;
                if (delta > 0) {
                    deltas.push({
                        className,
                        defaultSec: dSec,
                        variantSec: vSec,
                        deltaSec: delta,
                        ratio: dSec > 0 ? vSec / dSec : null,
                    });
                }
            }
            deltas.sort((a, b) => b.deltaSec - a.deltaSec);
            const top20 = deltas.slice(0, 20);

            lines.push('');
            lines.push(`## Top ${top20.length} Growing Test Classes: \`${v}\` vs \`default\``);
            lines.push('');
            if (top20.length === 0) {
                lines.push('No test class showed a positive duration increase.');
            } else {
                lines.push(`| # | Class | default Median (s) | ${v} Median (s) | Delta (s) | Ratio |`);
                lines.push('| ---: | :--- | ---: | ---: | ---: | ---: |');
                top20.forEach((row, idx) => {
                    const ratioStr = row.ratio === null ? 'n/a' : `${row.ratio.toFixed(2)}x`;
                    lines.push(
                        `| ${idx + 1} | \`${row.className}\` | ${row.defaultSec.toFixed(3)} | ${row.variantSec.toFixed(3)} | +${row.deltaSec.toFixed(3)} | ${ratioStr} |`
                    );
                });
            }
        }
    }

    lines.push('');
    return lines.join('\n');
}

function computeClassMedians(variantRuns) {
    const classes = new Set();
    for (const r of variantRuns) {
        for (const cls of r.classSums.keys()) {
            classes.add(cls);
        }
    }
    const medians = new Map();
    for (const cls of classes) {
        medians.set(cls, median(variantRuns.map((r) => r.classSums.get(cls) ?? 0)));
    }
    return medians;
}

function parseBenchTsv(text) {
    const rows = [];
    for (const rawLine of text.split(/\r?\n/)) {
        const line = rawLine.trim();
        if (line === '' || line.startsWith('#') || line.startsWith('label\t')) {
            continue;
        }
        const cols = line.split('\t');
        if (cols.length < 10) {
            throw new Error(`Expected 10 tab-separated columns in bench.tsv, got ${cols.length}: '${line}'`);
        }
        rows.push({
            label: cols[0],
            path: cols[1],
            kernel: cols[2],
            block: Number.parseInt(cols[3], 10),
            rounds: Number.parseInt(cols[4], 10),
            work: Number.parseInt(cols[5], 10),
            grid: Number.parseInt(cols[6], 10),
            firstMs: Number.parseFloat(cols[7]),
            minMs: Number.parseFloat(cols[8]),
            medianMs: Number.parseFloat(cols[9]),
        });
    }
    return rows;
}

function formatMs(value) {
    if (value === null || value === undefined || Number.isNaN(value)) {
        return 'n/a';
    }
    return value.toFixed(3);
}

function buildKernelSummary({ config, rows, runResults }) {
    const byRegime = new Map();
    const order = [];
    for (const row of rows) {
        const key = `${row.path}|${row.kernel}|${row.block}|${row.rounds}|${row.work}|${row.grid}`;
        let entry = byRegime.get(key);
        if (!entry) {
            entry = {
                path: row.path,
                kernel: row.kernel,
                block: row.block,
                rounds: row.rounds,
                work: row.work,
                grid: row.grid,
                byLabel: new Map(),
            };
            byRegime.set(key, entry);
            order.push(entry);
        }
        entry.byLabel.set(row.label, row);
    }

    const lines = [
        `## Kernel Micro-Benchmark (${config})`,
        '',
        `- Filter: \`${KERNEL_BENCH_FILTER}\``,
    ];
    if (runResults && runResults.length > 0) {
        for (const r of runResults) {
            lines.push(`- \`${r.variant}\`: ${r.passed ? 'PASS' : `FAIL (exit ${r.exitCode})`} (${r.wallSeconds.toFixed(1)} s)`);
        }
    }
    lines.push('');
    lines.push('| Path | Kernel | Block | Rounds | Work | Grid | State Machine 1st (ms) | Stack 1st (ms) | 1st Ratio (stack/sm) | State Machine Median (ms) | Stack Median (ms) | Median Ratio (stack/sm) |');
    lines.push('| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |');

    for (const entry of order) {
        const sm = entry.byLabel.get('default');
        const st = entry.byLabel.get('both-stack');
        const smFirst = sm ? sm.firstMs : null;
        const stFirst = st ? st.firstMs : null;
        const smMed = sm ? sm.medianMs : null;
        const stMed = st ? st.medianMs : null;
        lines.push(
            `| ${entry.path} | ${entry.kernel} | ${entry.block} | ${entry.rounds} | ${entry.work} | ${entry.grid} | ${formatMs(smFirst)} | ${formatMs(stFirst)} | ${formatRatio(stFirst, smFirst)} | ${formatMs(smMed)} | ${formatMs(stMed)} | ${formatRatio(stMed, smMed)} |`
        );
    }
    lines.push('');
    return lines.join('\n');
}

function runSuiteMode(options) {
    fs.mkdirSync(options.outDir, { recursive: true });
    const trxBaseDir = path.join(options.outDir, 'trx');
    const msbuildArgs = getMsbuildArgs();

    const buildRecords = [];
    const builtOk = new Map();
    for (const variant of options.variants) {
        process.stdout.write(`\n=== Building ${variant} (${options.config}) ===\n`);
        const args = [
            'build',
            cpuTestProject,
            ...msbuildArgs,
            '-c',
            options.config,
            '-nodeReuse:false',
            ...VARIANT_PROPS[variant],
        ];
        const res = runDotnet(args);
        const ok = res.exitCode === 0;
        builtOk.set(variant, ok);
        buildRecords.push({ variant, ok, exitCode: res.exitCode, wallSeconds: res.wallSeconds });
        if (!ok) {
            process.exitCode = 1;
        }
    }

    const runRecords = [];
    for (let run = 1; run <= options.runs; run++) {
        for (const variant of options.variants) {
            if (!builtOk.get(variant)) {
                runRecords.push({
                    variant,
                    run,
                    buildFailed: true,
                    passed: false,
                    exitCode: 1,
                    wallSeconds: 0,
                    xunitWallSeconds: 0,
                    counts: { total: 0, passed: 0, failed: 0, skipped: 0 },
                    classSums: new Map(),
                });
                continue;
            }

            const runTrxDir = path.join(trxBaseDir, `${variant}-run${run}`);
            fs.rmSync(runTrxDir, { recursive: true, force: true });
            fs.mkdirSync(runTrxDir, { recursive: true });

            process.stdout.write(`\n=== Running ${variant} run ${run}/${options.runs} (${options.config}) ===\n`);
            const args = [
                'test',
                cpuTestProject,
                '--no-build',
                ...msbuildArgs,
                '-c',
                options.config,
                '-nodeReuse:false',
                ...VARIANT_PROPS[variant],
                '--filter',
                SUITE_FILTER,
                '--logger',
                'trx',
                '--results-directory',
                runTrxDir,
                '--blame-crash',
                '--blame-crash-dump-type',
                'mini',
            ];
            const res = runDotnet(args);
            const trxFiles = findTrxFiles(runTrxDir);
            const parsed = summarizeRunTrxFiles(trxFiles);
            const passed = res.exitCode === 0 && parsed.counts.failed === 0 && trxFiles.length > 0;
            if (!passed) {
                process.exitCode = 1;
            }
            runRecords.push({
                variant,
                run,
                buildFailed: false,
                passed,
                exitCode: res.exitCode,
                wallSeconds: res.wallSeconds,
                xunitWallSeconds: parsed.xunitWallSeconds,
                counts: parsed.counts,
                classSums: parsed.classSums,
            });
        }
    }

    const summary = buildSuiteSummary({
        config: options.config,
        runsCount: options.runs,
        variants: options.variants,
        buildRecords,
        runRecords,
    });
    const summaryPath = path.join(options.outDir, 'summary.md');
    fs.writeFileSync(summaryPath, summary, 'utf8');
    process.stdout.write(`\nSummary written to ${summaryPath}\n`);
}

function runKernelsMode(options) {
    fs.mkdirSync(options.outDir, { recursive: true });
    const benchTsv = path.join(options.outDir, 'bench.tsv');
    const sidecarFile = `${benchTsv}.snapshots.json`;
    fs.rmSync(benchTsv, { force: true });
    fs.rmSync(sidecarFile, { force: true });

    const msbuildArgs = getMsbuildArgs();
    const kernelVariants = ['default', 'both-stack'];
    const builtOk = new Map();

    for (const variant of kernelVariants) {
        process.stdout.write(`\n=== Building ${variant} (${options.config}) for kernel benchmarks ===\n`);
        const args = [
            'build',
            cpuTestProject,
            ...msbuildArgs,
            '-c',
            options.config,
            '-nodeReuse:false',
            ...VARIANT_PROPS[variant],
        ];
        const res = runDotnet(args);
        builtOk.set(variant, res.exitCode === 0);
        if (res.exitCode !== 0) {
            process.exitCode = 1;
        }
    }

    const runResults = [];
    for (const variant of kernelVariants) {
        if (!builtOk.get(variant)) {
            runResults.push({ variant, passed: false, exitCode: 1, wallSeconds: 0 });
            continue;
        }
        process.stdout.write(`\n=== Running BarrierModelBenchmark for ${variant} (${options.config}) ===\n`);
        const args = [
            'test',
            cpuTestProject,
            '--no-build',
            ...msbuildArgs,
            '-c',
            options.config,
            '-nodeReuse:false',
            ...VARIANT_PROPS[variant],
            '--filter',
            KERNEL_BENCH_FILTER,
            '--blame-crash',
            '--blame-crash-dump-type',
            'mini',
        ];
        const res = runDotnet(args, {
            GSHARP_BARRIER_BENCH_OUT: benchTsv,
            GSHARP_BARRIER_BENCH_LABEL: variant,
        });
        const passed = res.exitCode === 0;
        if (!passed) {
            process.exitCode = 1;
        }
        runResults.push({ variant, passed, exitCode: res.exitCode, wallSeconds: res.wallSeconds });
    }

    const rows = fs.existsSync(benchTsv) ? parseBenchTsv(fs.readFileSync(benchTsv, 'utf8')) : [];
    const section = buildKernelSummary({ config: options.config, rows, runResults });
    const summaryPath = path.join(options.outDir, 'summary.md');
    const existing = fs.existsSync(summaryPath) ? fs.readFileSync(summaryPath, 'utf8') : '';
    const combined = existing === '' ? section : `${existing.trimEnd()}\n\n${section}`;
    fs.writeFileSync(summaryPath, combined, 'utf8');
    process.stdout.write(`\nKernel benchmark table written to ${summaryPath}\n`);
}

function runSelfTest() {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gsharp-time-barrier-'));
    try {
        const trxDefault = path.join(tmpDir, 'default-run1.trx');
        const trxStack = path.join(tmpDir, 'both-stack-run1.trx');

        fs.writeFileSync(
            trxDefault,
            [
                '<?xml version="1.0" encoding="utf-8"?>',
                '<TestRun>',
                '  <Times start="2026-11-26T00:00:00.0000000Z" finish="2026-11-26T00:00:10.0000000Z" />',
                '  <Results>',
                '    <UnitTestResult testId="id1" testName="ClassA.Fast" duration="00:00:01.0000000" outcome="Passed" />',
                '    <UnitTestResult testId="id2" testName="ClassB.Slow" duration="00:00:02.0000000" outcome="Passed" />',
                '  </Results>',
                '  <TestDefinitions>',
                '    <UnitTest id="id1" storage="Tests.Cpu.dll"><TestMethod codeBase="Tests.Cpu.dll" className="Kassyi.GSharp.Tests.Cpu.ClassA" name="Fast" /></UnitTest>',
                '    <UnitTest id="id2" storage="Tests.Cpu.dll"><TestMethod codeBase="Tests.Cpu.dll" className="Kassyi.GSharp.Tests.Cpu.ClassB" name="Slow" /></UnitTest>',
                '  </TestDefinitions>',
                '  <ResultSummary outcome="Completed"><Counters total="3" executed="2" passed="2" failed="0" notExecuted="1" /></ResultSummary>',
                '</TestRun>',
            ].join('\n'),
            'utf8'
        );

        fs.writeFileSync(
            trxStack,
            [
                '<?xml version="1.0" encoding="utf-8"?>',
                '<TestRun>',
                '  <Times start="2026-11-26T00:01:00.0000000Z" finish="2026-11-26T00:01:15.0000000Z" />',
                '  <Results>',
                '    <UnitTestResult testId="id1" testName="ClassA.Fast" duration="00:00:01.5000000" outcome="Passed" />',
                '    <UnitTestResult testId="id2" testName="ClassB.Slow" duration="00:00:08.0000000" outcome="Passed" />',
                '  </Results>',
                '  <TestDefinitions>',
                '    <UnitTest id="id1" storage="Tests.Cpu.dll"><TestMethod codeBase="Tests.Cpu.dll" className="Kassyi.GSharp.Tests.Cpu.ClassA" name="Fast" /></UnitTest>',
                '    <UnitTest id="id2" storage="Tests.Cpu.dll"><TestMethod codeBase="Tests.Cpu.dll" className="Kassyi.GSharp.Tests.Cpu.ClassB" name="Slow" /></UnitTest>',
                '  </TestDefinitions>',
                '  <ResultSummary outcome="Completed"><Counters total="3" executed="2" passed="2" failed="0" notExecuted="1" /></ResultSummary>',
                '</TestRun>',
            ].join('\n'),
            'utf8'
        );

        const defaultSum = summarizeRunTrxFiles([trxDefault]);
        const stackSum = summarizeRunTrxFiles([trxStack]);
        assert.strictEqual(defaultSum.xunitWallSeconds, 10);
        assert.strictEqual(stackSum.xunitWallSeconds, 15);
        assert.strictEqual(defaultSum.counts.passed, 2);
        assert.strictEqual(defaultSum.counts.skipped, 1);
        assert.strictEqual(defaultSum.classSums.get('Kassyi.GSharp.Tests.Cpu.ClassB'), 2);
        assert.strictEqual(stackSum.classSums.get('Kassyi.GSharp.Tests.Cpu.ClassB'), 8);

        const summary = buildSuiteSummary({
            config: 'Release',
            runsCount: 1,
            variants: ['default', 'both-stack'],
            buildRecords: [
                { variant: 'default', ok: true, exitCode: 0, wallSeconds: 5.0 },
                { variant: 'both-stack', ok: true, exitCode: 0, wallSeconds: 6.0 },
            ],
            runRecords: [
                {
                    variant: 'default',
                    run: 1,
                    buildFailed: false,
                    passed: true,
                    exitCode: 0,
                    wallSeconds: 12.0,
                    xunitWallSeconds: defaultSum.xunitWallSeconds,
                    counts: defaultSum.counts,
                    classSums: defaultSum.classSums,
                },
                {
                    variant: 'both-stack',
                    run: 1,
                    buildFailed: false,
                    passed: true,
                    exitCode: 0,
                    wallSeconds: 18.0,
                    xunitWallSeconds: stackSum.xunitWallSeconds,
                    counts: stackSum.counts,
                    classSums: stackSum.classSums,
                },
            ],
        });

        assert.ok(summary.includes('| both-stack | 1/1 | 18.00 | 1.50x | 15.00 | 1.50x |'), summary);
        assert.ok(summary.includes('| 1 | `Kassyi.GSharp.Tests.Cpu.ClassB` | 2.000 | 8.000 | +6.000 | 4.00x |'), summary);
        assert.ok(summary.includes('| 2 | `Kassyi.GSharp.Tests.Cpu.ClassA` | 1.000 | 1.500 | +0.500 | 1.50x |'), summary);

        const benchTsv = path.join(tmpDir, 'bench.tsv');
        fs.writeFileSync(
            benchTsv,
            [
                'label\tpath\tkernel\tblock\trounds\twork\tgrid\tfirstMs\tminMs\tmedianMs',
                'default\tnative\tbarrier\t256\t10\t0\t1\t0.500\t0.150\t0.200',
                'both-stack\tnative\tbarrier\t256\t10\t0\t1\t2.000\t1.500\t1.600',
            ].join('\n'),
            'utf8'
        );
        const kernelSection = buildKernelSummary({
            config: 'Release',
            rows: parseBenchTsv(fs.readFileSync(benchTsv, 'utf8')),
            runResults: [
                { variant: 'default', passed: true, exitCode: 0, wallSeconds: 1.0 },
                { variant: 'both-stack', passed: true, exitCode: 0, wallSeconds: 2.0 },
            ],
        });
        assert.ok(
            kernelSection.includes('| native | barrier | 256 | 10 | 0 | 1 | 0.500 | 2.000 | 4.00x | 0.200 | 1.600 | 8.00x |'),
            kernelSection
        );
    } finally {
        fs.rmSync(tmpDir, { recursive: true, force: true });
    }

    process.stdout.write('time-barrier-models self-test passed\n');
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    if (options.mode === 'self-test') {
        runSelfTest();
        return;
    }
    if (options.mode === 'kernels') {
        runKernelsMode(options);
        return;
    }
    runSuiteMode(options);
}

if (require.main === module) {
    main();
} else {
    module.exports = {
        buildKernelSummary,
        buildSuiteSummary,
        median,
        parseBenchTsv,
        readTrxFile,
        summarizeRunTrxFiles,
    };
}
