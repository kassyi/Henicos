#!/usr/bin/env node
// Compares per-launch JSONL differential records between the default (state-machine) and stack barrier-model builds.
//
// Usage:
//   node scripts/human/compare-barrier-models.js --base <dir> --stack <dir> [--models native|managed|both] [--allow <file>] [--report <file>]
//   node scripts/human/compare-barrier-models.js --run [--models native|managed|both] [--allow <file>] [--report <file>] [-- <dotnet-test-args>...]
//   node scripts/human/compare-barrier-models.js --self-test
//   node scripts/human/compare-barrier-models.js --help
'use strict';

const assert = require('assert');
const { spawnSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..', '..');
const defaultAllowFile = path.join(
    repoRoot,
    'tests',
    'Compiler',
    'Kassyi.GSharp.Tests.Shared',
    'Differential',
    'Harness',
    'BarrierModelDivergences.json'
);
const cpuTestProject = path.join(
    'tests',
    'Compiler',
    'Kassyi.GSharp.Tests.Cpu',
    'Kassyi.GSharp.Tests.Cpu.csproj'
);

const VALID_MODELS = new Set(['native', 'managed', 'both']);
const VALID_ALLOW_PATHS = new Set(['Native', 'Managed']);
const ONE_SIDED_MAX_RATIO = 0.05;

const helpText = [
    'Usage:',
    '  node scripts/human/compare-barrier-models.js --base <dir> --stack <dir> [--models native|managed|both] [--allow <file>] [--report <file>]',
    '  node scripts/human/compare-barrier-models.js --run [--models native|managed|both] [--allow <file>] [--report <file>] [-- <dotnet-test-args>...]',
    '  node scripts/human/compare-barrier-models.js --self-test',
    '',
    'Options:',
    '  --base <dir>     Directory containing JSONL records from the default (state-machine) build.',
    '  --stack <dir>    Directory containing JSONL records from the stack build.',
    '  --models <mode>  Which path(s) to compare: native (default), managed, or both.',
    '  --allow <file>   JSON allowlist of known divergences (default: BarrierModelDivergences.json). An entry with',
    '                   "undefinedBehavior": true reads undefined values (an unchecked access): it may match on one',
    '                   platform and not on another, so it is never reported as stale.',
    '  --report <file>  Optional path to write the comparison report (.json for JSON, otherwise text).',
    '  --run            Run Tests.Cpu Differential tests in default and variant builds, then compare.',
    '  --self-test      Run built-in unit tests on in-memory and temporary JSONL records.',
    '  --help, -h       Show this help.',
].join('\n');

function fail(message) {
    process.stderr.write(`Error: ${message}\n`);
    process.exit(1);
}

function parseArgs(argv) {
    const options = {
        mode: 'compare',
        base: null,
        stack: null,
        models: 'native',
        allow: defaultAllowFile,
        report: null,
        testArgs: [],
    };

    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            process.stdout.write(helpText + '\n');
            process.exit(0);
        } else if (arg === '--self-test') {
            options.mode = 'self-test';
        } else if (arg === '--run') {
            options.mode = 'run';
        } else if (arg === '--') {
            options.testArgs.push(...argv.slice(i + 1));
            break;
        } else if (arg === '--base' || arg === '--stack' || arg === '--models' || arg === '--allow' || arg === '--report') {
            const value = argv[++i];
            if (value === undefined) {
                fail(`${arg} requires a value.\n\n${helpText}`);
            }
            const key = arg.slice(2);
            options[key] = (key === 'base' || key === 'stack' || key === 'allow' || key === 'report')
                ? path.resolve(value)
                : value;
        } else if (arg.startsWith('--models=')) {
            options.models = arg.slice('--models='.length);
        } else if (arg.startsWith('--base=')) {
            options.base = path.resolve(arg.slice('--base='.length));
        } else if (arg.startsWith('--stack=')) {
            options.stack = path.resolve(arg.slice('--stack='.length));
        } else if (arg.startsWith('--allow=')) {
            options.allow = path.resolve(arg.slice('--allow='.length));
        } else if (arg.startsWith('--report=')) {
            options.report = path.resolve(arg.slice('--report='.length));
        } else {
            fail(`Unknown argument: ${arg}\n\n${helpText}`);
        }
    }

    if (!VALID_MODELS.has(options.models)) {
        fail(`Invalid --models '${options.models}'. Expected native, managed, or both.`);
    }

    if (options.mode === 'compare' && (!options.base || !options.stack)) {
        fail(`Compare mode requires both --base and --stack.\n\n${helpText}`);
    }

    return options;
}

function selectedPathsForModels(models) {
    if (models === 'native') {
        return new Set(['Native']);
    }
    if (models === 'managed') {
        return new Set(['Managed']);
    }
    if (models === 'both') {
        return new Set(['Native', 'Managed']);
    }
    throw new Error(`Invalid models mode: ${models}`);
}

function parseAllowlistText(text, sourceLabel) {
    let parsed;
    try {
        parsed = JSON.parse(text);
    } catch (err) {
        throw new Error(`Invalid JSON in allowlist ${sourceLabel}: ${err.message}`);
    }
    if (!Array.isArray(parsed)) {
        throw new Error(`Allowlist ${sourceLabel} must be a JSON array.`);
    }
    const seen = new Set();
    const entries = [];
    for (let i = 0; i < parsed.length; i++) {
        const entry = parsed[i];
        if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) {
            throw new Error(`Allowlist ${sourceLabel}[${i}] must be an object.`);
        }
        const allowedKeys = new Set(['case', 'path', 'issue', 'reason', 'undefinedBehavior']);
        for (const k of Object.keys(entry)) {
            if (!allowedKeys.has(k)) {
                throw new Error(`Allowlist ${sourceLabel}[${i}] has unknown field '${k}'.`);
            }
        }
        if (typeof entry.case !== 'string' || entry.case.trim() === '') {
            throw new Error(`Allowlist ${sourceLabel}[${i}].case must be a non-empty string.`);
        }
        if (!VALID_ALLOW_PATHS.has(entry.path)) {
            throw new Error(`Allowlist ${sourceLabel}[${i}].path must be 'Native' or 'Managed'.`);
        }
        if (!Number.isInteger(entry.issue) || entry.issue <= 0) {
            throw new Error(`Allowlist ${sourceLabel}[${i}].issue must be a positive integer.`);
        }
        if (typeof entry.reason !== 'string' || entry.reason.trim() === '') {
            throw new Error(`Allowlist ${sourceLabel}[${i}].reason must be a non-empty string.`);
        }
        if (entry.undefinedBehavior !== undefined && typeof entry.undefinedBehavior !== 'boolean') {
            throw new Error(`Allowlist ${sourceLabel}[${i}].undefinedBehavior must be a boolean.`);
        }
        const pairKey = `${entry.case}\0${entry.path}`;
        if (seen.has(pairKey)) {
            throw new Error(`Duplicate allowlist entry for (${entry.case}, ${entry.path}) in ${sourceLabel}.`);
        }
        seen.add(pairKey);
        entries.push({
            case: entry.case,
            path: entry.path,
            issue: entry.issue,
            reason: entry.reason,
            undefinedBehavior: entry.undefinedBehavior === true,
        });
    }
    return entries;
}

function loadAllowlist(filePath) {
    if (!fs.existsSync(filePath)) {
        throw new Error(`Allowlist file not found: ${filePath}`);
    }
    return parseAllowlistText(fs.readFileSync(filePath, 'utf8'), filePath);
}

function makeRecordKey(rec) {
    return `${rec.case}\0${rec.seed}\0${rec.shape}\0${rec.path}\0${rec.ordinal}`;
}

function formatRecordKey(rec) {
    return `${rec.case} [seed=${rec.seed}, shape=${rec.shape}, path=${rec.path}, ordinal=${rec.ordinal}]`;
}

function validateRecord(rec, sourceLabel, lineNo) {
    if (rec === null || typeof rec !== 'object' || Array.isArray(rec)) {
        throw new Error(`${sourceLabel}:${lineNo}: record must be a JSON object.`);
    }
    if (typeof rec.case !== 'string' || rec.case === '') {
        throw new Error(`${sourceLabel}:${lineNo}: missing or invalid 'case'.`);
    }
    if (!Number.isInteger(rec.seed)) {
        throw new Error(`${sourceLabel}:${lineNo}: missing or invalid 'seed'.`);
    }
    if (!Number.isInteger(rec.shape) || rec.shape < 0) {
        throw new Error(`${sourceLabel}:${lineNo}: missing or invalid 'shape'.`);
    }
    if (typeof rec.path !== 'string' || rec.path === '') {
        throw new Error(`${sourceLabel}:${lineNo}: missing or invalid 'path'.`);
    }
    if (!Number.isInteger(rec.ordinal) || rec.ordinal < 0) {
        throw new Error(`${sourceLabel}:${lineNo}: missing or invalid 'ordinal'.`);
    }
    if (!Number.isInteger(rec.stackLaunches) || rec.stackLaunches < 0) {
        throw new Error(`${sourceLabel}:${lineNo}: missing or invalid 'stackLaunches'.`);
    }
}

function parseJsonlLines(text, sourceLabel, allowedPaths, targetMap) {
    const lines = text.split(/\r?\n/);
    for (let idx = 0; idx < lines.length; idx++) {
        const line = lines[idx].trim();
        if (line === '') {
            continue;
        }
        let rec;
        try {
            rec = JSON.parse(line);
        } catch (err) {
            throw new Error(`${sourceLabel}:${idx + 1}: invalid JSON: ${err.message}`);
        }
        validateRecord(rec, sourceLabel, idx + 1);
        if (!allowedPaths.has(rec.path)) {
            continue;
        }
        const key = makeRecordKey(rec);
        if (targetMap.has(key)) {
            throw new Error(`${sourceLabel}:${idx + 1}: duplicate key ${formatRecordKey(rec)}.`);
        }
        targetMap.set(key, rec);
    }
}

function readRecordsDir(dirPath, models) {
    if (!fs.existsSync(dirPath) || !fs.statSync(dirPath).isDirectory()) {
        throw new Error(`Record directory does not exist: ${dirPath}`);
    }
    const files = fs.readdirSync(dirPath)
        .filter(name => name.endsWith('.jsonl'))
        .sort();
    if (files.length === 0) {
        throw new Error(`No *.jsonl files found in ${dirPath}`);
    }
    const allowedPaths = selectedPathsForModels(models);
    const records = new Map();
    for (const file of files) {
        const fullPath = path.join(dirPath, file);
        parseJsonlLines(fs.readFileSync(fullPath, 'utf8'), fullPath, allowedPaths, records);
    }
    return records;
}

function exceptionsEqual(a, b) {
    if (a === null && b === null) {
        return true;
    }
    if (a === null || b === null) {
        return false;
    }
    return a.type === b.type && a.message === b.message;
}

function compareRecordMaps(baseMap, stackMap, allowEntries, models) {
    const allowedPaths = selectedPathsForModels(models);
    const allowByPair = new Map();
    for (const entry of allowEntries) {
        if (!allowedPaths.has(entry.path)) {
            continue;
        }
        allowByPair.set(`${entry.case}\0${entry.path}`, {
            ...entry,
            matchedMismatch: false,
        });
    }

    const bothKeys = [];
    const baseOnly = [];
    const stackOnly = [];
    for (const [key, baseRec] of baseMap.entries()) {
        if (stackMap.has(key)) {
            bothKeys.push(key);
        } else {
            baseOnly.push(baseRec);
        }
    }
    for (const [key, stackRec] of stackMap.entries()) {
        if (!baseMap.has(key)) {
            stackOnly.push(stackRec);
        }
    }

    const totalKeys = bothKeys.length + baseOnly.length + stackOnly.length;
    const oneSidedCount = baseOnly.length + stackOnly.length;
    const oneSidedRatio = totalKeys === 0 ? 0 : oneSidedCount / totalKeys;

    const perCase = new Map();
    const ensureCaseStats = (caseName) => {
        let stats = perCase.get(caseName);
        if (!stats) {
            stats = {
                case: caseName,
                compared: 0,
                withStackLaunches: 0,
                mismatches: 0,
                allowedMismatches: 0,
                oneSided: 0,
            };
            perCase.set(caseName, stats);
        }
        return stats;
    };

    for (const rec of baseOnly) {
        ensureCaseStats(rec.case).oneSided++;
    }
    for (const rec of stackOnly) {
        ensureCaseStats(rec.case).oneSided++;
    }

    let withStackLaunches = 0;
    const mismatches = [];
    const allowedMismatches = [];
    const comparedCasePaths = new Set();

    bothKeys.sort();
    for (const key of bothKeys) {
        const baseRec = baseMap.get(key);
        const stackRec = stackMap.get(key);
        const stats = ensureCaseStats(baseRec.case);
        stats.compared++;
        comparedCasePaths.add(`${baseRec.case}\0${baseRec.path}`);
        if (stackRec.stackLaunches > 0) {
            withStackLaunches++;
            stats.withStackLaunches++;
        }

        const diffs = [];
        if ((baseRec.outputs ?? null) !== (stackRec.outputs ?? null)) {
            diffs.push('outputs');
        }
        if ((baseRec.ptrOutputs ?? null) !== (stackRec.ptrOutputs ?? null)) {
            diffs.push('ptrOutputs');
        }
        if (!exceptionsEqual(baseRec.exception ?? null, stackRec.exception ?? null)) {
            diffs.push('exception');
        }

        if (diffs.length > 0) {
            const detail = {
                key: formatRecordKey(baseRec),
                case: baseRec.case,
                seed: baseRec.seed,
                shape: baseRec.shape,
                path: baseRec.path,
                ordinal: baseRec.ordinal,
                diffs,
                base: {
                    outputs: baseRec.outputs ?? null,
                    ptrOutputs: baseRec.ptrOutputs ?? null,
                    exception: baseRec.exception ?? null,
                },
                stack: {
                    outputs: stackRec.outputs ?? null,
                    ptrOutputs: stackRec.ptrOutputs ?? null,
                    exception: stackRec.exception ?? null,
                },
            };
            const allowEntry = allowByPair.get(`${baseRec.case}\0${baseRec.path}`);
            if (allowEntry) {
                allowEntry.matchedMismatch = true;
                stats.allowedMismatches++;
                allowedMismatches.push({ ...detail, issue: allowEntry.issue, reason: allowEntry.reason });
            } else {
                stats.mismatches++;
                mismatches.push(detail);
            }
        }
    }

    const staleAllowEntries = [];
    for (const [pairKey, entry] of allowByPair.entries()) {
        // An entry for undefined behavior may match on one platform and not on another: it is never stale
        if (comparedCasePaths.has(pairKey) && !entry.matchedMismatch && !entry.undefinedBehavior) {
            staleAllowEntries.push({
                case: entry.case,
                path: entry.path,
                issue: entry.issue,
                reason: entry.reason,
            });
        }
    }

    const failures = [];
    if (bothKeys.length === 0) {
        failures.push('compared nothing (0 matching keys between base and stack)');
    } else if (withStackLaunches === 0) {
        failures.push('compared nothing (stackLaunches == 0 for all compared keys)');
    }
    if (oneSidedRatio > ONE_SIDED_MAX_RATIO) {
        failures.push(
            `one-sided keys exceed 5% (${oneSidedCount}/${totalKeys} = ${(oneSidedRatio * 100).toFixed(2)}%)`
        );
    }
    if (mismatches.length > 0) {
        failures.push(`${mismatches.length} unexcused mismatch(es)`);
    }
    if (staleAllowEntries.length > 0) {
        failures.push(`${staleAllowEntries.length} stale allowlist entry(ies)`);
    }

    const caseTable = [...perCase.values()].sort((a, b) => a.case.localeCompare(b.case));

    return {
        ok: failures.length === 0,
        models,
        compared: bothKeys.length,
        withStackLaunches,
        mismatchesCount: mismatches.length,
        allowedMismatchesCount: allowedMismatches.length,
        oneSidedCount,
        baseOnlyCount: baseOnly.length,
        stackOnlyCount: stackOnly.length,
        totalKeys,
        oneSidedPct: totalKeys === 0 ? 0 : (oneSidedCount / totalKeys) * 100,
        failures,
        caseTable,
        mismatches,
        allowedMismatches,
        baseOnly: baseOnly.map(r => ({ case: r.case, seed: r.seed, shape: r.shape, path: r.path, ordinal: r.ordinal })),
        stackOnly: stackOnly.map(r => ({ case: r.case, seed: r.seed, shape: r.shape, path: r.path, ordinal: r.ordinal })),
        staleAllowEntries,
    };
}

function formatReport(result) {
    const lines = [];
    lines.push(`Barrier-model comparison (${result.models}): ${result.ok ? 'PASS' : 'FAIL'}`);
    lines.push(`  compared keys      : ${result.compared}`);
    lines.push(`  with stack launches: ${result.withStackLaunches}`);
    lines.push(`  mismatches         : ${result.mismatchesCount}`);
    lines.push(`  allowed mismatches : ${result.allowedMismatchesCount}`);
    lines.push(`  one-sided keys     : ${result.oneSidedCount}/${result.totalKeys} (${result.oneSidedPct.toFixed(2)}%)`);

    if (result.oneSidedCount > 0) {
        lines.push('');
        lines.push(`Warning: ${result.oneSidedCount} one-sided key(s) (base-only=${result.baseOnlyCount}, stack-only=${result.stackOnlyCount}):`);
        const oneSidedCases = new Map();
        for (const r of result.baseOnly) {
            const k = `${r.case} (${r.path}, base-only)`;
            oneSidedCases.set(k, (oneSidedCases.get(k) || 0) + 1);
        }
        for (const r of result.stackOnly) {
            const k = `${r.case} (${r.path}, stack-only)`;
            oneSidedCases.set(k, (oneSidedCases.get(k) || 0) + 1);
        }
        for (const [label, count] of [...oneSidedCases.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
            lines.push(`  - ${label}: ${count}`);
        }
    }

    if (result.caseTable.length > 0) {
        lines.push('');
        lines.push('Per-case summary:');
        lines.push('  Case | Compared | StackLaunches>0 | Mismatches | Allowed | OneSided');
        for (const row of result.caseTable) {
            lines.push(
                `  ${row.case} | ${row.compared} | ${row.withStackLaunches} | ${row.mismatches} | ${row.allowedMismatches} | ${row.oneSided}`
            );
        }
    }

    if (result.mismatches.length > 0) {
        lines.push('');
        lines.push('Unexcused mismatches:');
        for (const m of result.mismatches) {
            lines.push(`  * ${m.key} [${m.diffs.join(', ')}]`);
            lines.push(`    base : ${JSON.stringify(m.base)}`);
            lines.push(`    stack: ${JSON.stringify(m.stack)}`);
        }
    }

    if (result.allowedMismatches.length > 0) {
        lines.push('');
        lines.push('Allowed mismatches:');
        for (const m of result.allowedMismatches) {
            lines.push(`  * ${m.key} [${m.diffs.join(', ')}] (#${m.issue}: ${m.reason})`);
        }
    }

    if (result.staleAllowEntries.length > 0) {
        lines.push('');
        lines.push('Stale allowlist entries (matched on all compared keys):');
        for (const s of result.staleAllowEntries) {
            lines.push(`  * (${s.case}, ${s.path}) #${s.issue}: ${s.reason}`);
        }
    }

    if (result.failures.length > 0) {
        lines.push('');
        lines.push(`Failures: ${result.failures.join('; ')}`);
    }

    return lines.join('\n');
}

function runDotnetTest(extraProps, recordDir, extraTestArgs) {
    fs.rmSync(recordDir, { recursive: true, force: true });
    fs.mkdirSync(recordDir, { recursive: true });

    const hasCustomFilter = extraTestArgs.some(a => a === '--filter' || a.startsWith('--filter='));
    const msbuildArgs = (process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS || '')
        .split(/\s+/)
        .filter(Boolean);

    const args = [
        'test',
        cpuTestProject,
        ...msbuildArgs,
        '-nodeReuse:false',
        ...extraProps,
        ...(hasCustomFilter ? [] : ['--filter', 'Category=Differential&Category!=Stress']),
        '--blame-crash',
        '--blame-crash-dump-type',
        'mini',
        ...extraTestArgs,
    ];

    const env = {
        ...process.env,
        DiffEngine_Disabled: 'true',
        CI: 'true',
        GSHARP_TEST_NO_CUDA: '1',
        GSHARP_DIFF_RECORD: recordDir,
    };

    const res = spawnSync('dotnet', args, { cwd: repoRoot, env, stdio: 'inherit' });
    if (res.error) {
        fail(`Failed to run dotnet test: ${res.error.message}`);
    }
    if ((res.status ?? 1) !== 0) {
        process.exit(res.status ?? 1);
    }
}

function variantConfigForModels(models) {
    if (models === 'native') {
        return {
            dirName: 'stack',
            props: ['-p:GSharpNativeBarrierModel=Stack', '-p:GSharpOutputVariant=stack'],
        };
    }
    if (models === 'managed') {
        return {
            dirName: 'managed-stack',
            props: ['-p:GSharpManagedBarrierModel=Stack', '-p:GSharpOutputVariant=managed-stack'],
        };
    }
    return {
        dirName: 'both-stack',
        props: [
            '-p:GSharpNativeBarrierModel=Stack',
            '-p:GSharpManagedBarrierModel=Stack',
            '-p:GSharpOutputVariant=both-stack',
        ],
    };
}

function runSelfTest() {
    const makeRec = (overrides) => ({
        case: 'CaseA',
        seed: 0,
        shape: 0,
        path: 'Native',
        grid: [2, 1, 1],
        block: [64, 1, 1],
        ordinal: 0,
        outputs: 'sha256:aaa',
        ptrOutputs: null,
        exception: null,
        stackLaunches: 1,
        ...overrides,
    });

    const toMap = (recs, models) => {
        const map = new Map();
        const text = recs.map(r => JSON.stringify(r)).join('\n');
        parseJsonlLines(text, 'memory.jsonl', selectedPathsForModels(models), map);
        return map;
    };

    // 1. Equal records -> pass (also verifies --models filters out unselected Managed mismatches).
    {
        const base = toMap([
            makeRec({ case: 'CaseA', stackLaunches: 0 }),
            makeRec({ case: 'CaseA', path: 'Managed', outputs: 'sha256:m1', stackLaunches: 0 }),
        ], 'native');
        const stack = toMap([
            makeRec({ case: 'CaseA', stackLaunches: 1 }),
            makeRec({ case: 'CaseA', path: 'Managed', outputs: 'sha256:m2', stackLaunches: 0 }),
        ], 'native');
        const res = compareRecordMaps(base, stack, [], 'native');
        assert.strictEqual(res.ok, true, 'equal native records should pass even if unselected managed differs');
        assert.strictEqual(res.compared, 1);
        assert.strictEqual(res.withStackLaunches, 1);
        assert.strictEqual(res.mismatchesCount, 0);
    }

    // 2. One mismatch: outputs mismatch, ptrOutputs mismatch, and exception mismatch (catches mutant b).
    {
        const base = toMap([makeRec({ outputs: 'sha256:111' })], 'native');
        const stack = toMap([makeRec({ outputs: 'sha256:222' })], 'native');
        const res = compareRecordMaps(base, stack, [], 'native');
        assert.strictEqual(res.ok, false, 'outputs mismatch must fail');
        assert.strictEqual(res.mismatchesCount, 1);
        assert.deepStrictEqual(res.mismatches[0].diffs, ['outputs']);
    }
    {
        const base = toMap([makeRec({ ptrOutputs: 'sha256:p1' })], 'native');
        const stack = toMap([makeRec({ ptrOutputs: 'sha256:p2' })], 'native');
        const res = compareRecordMaps(base, stack, [], 'native');
        assert.strictEqual(res.ok, false, 'ptrOutputs mismatch must fail');
        assert.deepStrictEqual(res.mismatches[0].diffs, ['ptrOutputs']);
    }
    {
        const base = toMap([makeRec({ exception: { type: 'System.IndexOutOfRangeException', message: 'msg1' } })], 'native');
        const stack = toMap([makeRec({ exception: { type: 'System.IndexOutOfRangeException', message: 'msg2' } })], 'native');
        const res = compareRecordMaps(base, stack, [], 'native');
        assert.strictEqual(res.ok, false, 'exception message mismatch must fail');
        assert.strictEqual(res.mismatchesCount, 1);
        assert.deepStrictEqual(res.mismatches[0].diffs, ['exception']);
    }

    // 3. One-sided <= 5% (1 / 25 = 4%) -> pass with warning.
    {
        const baseList = [];
        const stackList = [];
        for (let i = 0; i < 24; i++) {
            baseList.push(makeRec({ seed: i, stackLaunches: 0 }));
            stackList.push(makeRec({ seed: i, stackLaunches: 1 }));
        }
        baseList.push(makeRec({ seed: 99, stackLaunches: 0 }));
        const res = compareRecordMaps(toMap(baseList, 'native'), toMap(stackList, 'native'), [], 'native');
        assert.strictEqual(res.ok, true, '4% one-sided keys should pass');
        assert.strictEqual(res.oneSidedCount, 1);
        assert.strictEqual(res.totalKeys, 25);
        assert.ok(formatReport(res).includes('Warning: 1 one-sided key(s)'));
    }

    // 4. One-sided > 5% (2 / 25 = 8%) -> fail.
    {
        const baseList = [];
        const stackList = [];
        for (let i = 0; i < 23; i++) {
            baseList.push(makeRec({ seed: i, stackLaunches: 0 }));
            stackList.push(makeRec({ seed: i, stackLaunches: 1 }));
        }
        baseList.push(makeRec({ seed: 98, stackLaunches: 0 }));
        stackList.push(makeRec({ seed: 99, stackLaunches: 1 }));
        const res = compareRecordMaps(toMap(baseList, 'native'), toMap(stackList, 'native'), [], 'native');
        assert.strictEqual(res.ok, false, '8% one-sided keys must fail');
        assert.strictEqual(res.oneSidedCount, 2);
    }

    // 5. Active allowlist entry passes; stale allowlist entry fails.
    {
        const allow = parseAllowlistText(
            JSON.stringify([{ case: 'CaseA', path: 'Native', issue: 2105, reason: 'known test divergence' }]),
            'test-allow.json'
        );
        const baseMismatch = toMap([makeRec({ outputs: 'sha256:111' })], 'native');
        const stackMismatch = toMap([makeRec({ outputs: 'sha256:222' })], 'native');
        const activeRes = compareRecordMaps(baseMismatch, stackMismatch, allow, 'native');
        assert.strictEqual(activeRes.ok, true, 'allowed mismatch should pass');
        assert.strictEqual(activeRes.allowedMismatchesCount, 1);
        assert.strictEqual(activeRes.staleAllowEntries.length, 0);

        const baseEqual = toMap([makeRec({ outputs: 'sha256:111' })], 'native');
        const stackEqual = toMap([makeRec({ outputs: 'sha256:111' })], 'native');
        const staleRes = compareRecordMaps(baseEqual, stackEqual, allow, 'native');
        assert.strictEqual(staleRes.ok, false, 'stale allowlist entry must fail');
        assert.strictEqual(staleRes.staleAllowEntries.length, 1);

        const ubAllow = parseAllowlistText(
            JSON.stringify([
                { case: 'CaseA', path: 'Native', issue: 2241, reason: 'reads out of range', undefinedBehavior: true },
            ]),
            'test-allow-ub.json'
        );
        const ubRes = compareRecordMaps(baseEqual, stackEqual, ubAllow, 'native');
        assert.strictEqual(ubRes.ok, true, 'an undefined-behavior entry that does not match must not be stale');
        assert.strictEqual(ubRes.staleAllowEntries.length, 0);
    }

    // 6. Zero stack launches -> fail ("compared nothing").
    {
        const base = toMap([makeRec({ stackLaunches: 0 })], 'native');
        const stack = toMap([makeRec({ stackLaunches: 0 })], 'native');
        const res = compareRecordMaps(base, stack, [], 'native');
        assert.strictEqual(res.ok, false, 'zero stack launches must fail');
        assert.ok(res.failures.some(f => f.includes('compared nothing')));
    }

    // 7. End-to-end directory reading & duplicate key rejection.
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gsharp-compare-barrier-'));
    try {
        const baseDir = path.join(tmpDir, 'base');
        const stackDir = path.join(tmpDir, 'stack');
        fs.mkdirSync(baseDir, { recursive: true });
        fs.mkdirSync(stackDir, { recursive: true });
        fs.writeFileSync(path.join(baseDir, 'a.jsonl'), JSON.stringify(makeRec({ stackLaunches: 0 })) + '\n');
        fs.writeFileSync(path.join(stackDir, 'b.jsonl'), JSON.stringify(makeRec({ stackLaunches: 2 })) + '\n');
        const res = compareRecordMaps(readRecordsDir(baseDir, 'native'), readRecordsDir(stackDir, 'native'), [], 'native');
        assert.strictEqual(res.ok, true);

        fs.writeFileSync(
            path.join(baseDir, 'dup.jsonl'),
            JSON.stringify(makeRec({ stackLaunches: 0 })) + '\n'
        );
        assert.throws(() => readRecordsDir(baseDir, 'native'), /duplicate key/);
    } finally {
        fs.rmSync(tmpDir, { recursive: true, force: true });
    }

    process.stdout.write('compare-barrier-models self-test passed (all 7 checks)\n');
}

function main() {
    const options = parseArgs(process.argv.slice(2));
    if (options.mode === 'self-test') {
        runSelfTest();
        return;
    }

    let baseDir = options.base;
    let stackDir = options.stack;
    if (options.mode === 'run') {
        const variant = variantConfigForModels(options.models);
        baseDir = path.join(repoRoot, 'tmp', 'diff-records', 'default');
        stackDir = path.join(repoRoot, 'tmp', 'diff-records', variant.dirName);
        runDotnetTest([], baseDir, options.testArgs);
        runDotnetTest(variant.props, stackDir, options.testArgs);
    }

    let baseMap;
    let stackMap;
    let allowEntries;
    try {
        allowEntries = loadAllowlist(options.allow);
        baseMap = readRecordsDir(baseDir, options.models);
        stackMap = readRecordsDir(stackDir, options.models);
    } catch (err) {
        fail(err.message);
    }

    const result = compareRecordMaps(baseMap, stackMap, allowEntries, options.models);
    const formatted = formatReport(result);
    process.stdout.write(formatted + '\n');

    if (options.report) {
        fs.mkdirSync(path.dirname(options.report), { recursive: true });
        const content = options.report.toLowerCase().endsWith('.json')
            ? JSON.stringify(result, null, 2) + '\n'
            : formatted + '\n';
        fs.writeFileSync(options.report, content, 'utf8');
    }

    if (!result.ok) {
        // exitCode, not exit(): exit() can end the process before a piped stdout (the CI log) is written
        process.exitCode = 1;
    }
}

if (require.main === module) {
    main();
}

module.exports = {
    compareRecordMaps,
    formatReport,
    loadAllowlist,
    parseAllowlistText,
    parseJsonlLines,
    readRecordsDir,
};
