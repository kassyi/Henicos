#!/usr/bin/env node
// Cross-checks the compiler-internal names that appear in the snapshot output against the places that spell them.
// Usage (from anywhere in the repository): node scripts/agent/check-internal-names.js
//   Section 1: __gsharp_* names in the output that no YAML value, Prelude declaration, source spelling or prefix explains.
//   Section 2: other __ names in the output, minus CUDA/C++ builtins, for a manual look.
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const compiler = path.join(repoRoot, 'src', 'Compiler', 'Kassyi.GSharp.Compiler');

function read(file) {
    return fs.readFileSync(file, 'utf8');
}

function matches(text, regex) {
    return [...text.matchAll(regex)].map(m => m[0]);
}

function listFiles(dir, filter, out = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            if (entry.name !== 'bin' && entry.name !== 'obj') {
                listFiles(full, filter, out);
            }
        } else if (filter(entry.name)) {
            out.push(full);
        }
    }
    return out;
}

const snapshots = execFileSync('git', ['ls-files', '*.verified.*'], { cwd: repoRoot, encoding: 'utf8' })
    .split('\n')
    .filter(file => file && !file.includes('Prelude.g.verified'))
    .map(file => read(path.join(repoRoot, file)));

// Renamed user declarations (__gsharp_id_ and friends) are the naming layer's business, not internal names.
const output = new Set(snapshots.flatMap(text => matches(text, /__gsharp_[A-Za-z0-9_]*/g))
    .filter(name => !/^__gsharp_(id|il|mb|fn|k|ty|tp)_/.test(name)));

const known = new Set([
    ...matches(read(path.join(compiler, 'Resources', 'InternalNames.yml')), /__gsharp_[a-z0-9_]+/g),
    // The Prelude sources, as listed in PreludeResourceFiles of the compiler project.
    ...['Prelude.cu', 'Prelude.cpp', 'StackLinq.cuh'].map(name => path.join(compiler, 'Resources', name))
        .concat(listFiles(path.join(compiler, 'Resources', 'Prelude'), name => name.endsWith('.hpp')))
        .flatMap(file => matches(read(file), /__gsharp_[A-Za-z0-9_]+/g)),
    // Native API definitions (PTX intrinsic wrappers) that generate .g.cs at build time.
    ...listFiles(path.join(repoRoot, 'src', 'NativeApi'), name => name.endsWith('.yml'))
        .flatMap(file => matches(read(file), /__gsharp_[A-Za-z0-9_]+/g)),
    ...[compiler, path.join(repoRoot, 'src', 'Core'), path.join(repoRoot, 'src', 'Shared')]
        .flatMap(dir => listFiles(dir, name => name.endsWith('.cs')))
        .flatMap(file => matches(read(file), /__gsharp_[A-Za-z0-9_]*/g)),
]);

// Prefixes (values ending in _), except the bare family prefix that every name shares.
const prefixes = [...known].filter(name => /_.+_$/.test(name) && name !== '__gsharp_');

console.log('== 1. __gsharp_ names not explained by any known spelling or prefix');
const unexplained = new Set();
for (const name of output) {
    if (known.has(name)) {
        continue;
    }
    // Lowering.LabelPrefix + a PascalCase label kind (BranchElse, ForStart, ...).
    if (/^__gsharp_[A-Z]/.test(name)) {
        continue;
    }
    if (prefixes.some(prefix => name.startsWith(prefix))) {
        continue;
    }
    unexplained.add(name.replace(/_[0-9]+$/, '_N'));
}
[...unexplained].sort().forEach(name => console.log(name));

console.log();
console.log('== 2. other __ names (builtins filtered out)');
const builtins = new RegExp('^__(declspec|device__|global__|shared__|constant__|forceinline(__)?|noinline__|restrict(__)?'
    + '|launch_bounds__|syncthreads|syncwarp|shfl[A-Za-z0-9_]*|match[A-Za-z0-9_]*|reduce[A-Za-z0-9_]*|ballot_sync|any_sync'
    + '|all_sync|activemask|threadfence[A-Za-z0-9_]*|popc[A-Za-z0-9_]*|clz[A-Za-z0-9_]*|ffs[A-Za-z0-9_]*|brev[A-Za-z0-9_]*'
    + '|ldg|ldcg|stcg|cvta[A-Za-z0-9_]*|dp4a|funnelshift[A-Za-z0-9_]*|nanosleep|builtin_[A-Za-z0-9_]*|attribute__'
    + '|clang[A-Za-z0-9_]*|GNUC[A-Za-z0-9_]*|expf|logf|fsqrt[A-Za-z0-9_]*|u?mul64hi|mulhi|usad|sad|smid|prefetch[A-Za-z0-9_]*'
    + '|lane_id|warp_id|byte_perm|applypriority)$');
const counts = new Map();
for (const text of snapshots) {
    for (const m of text.matchAll(/(?:^|[^A-Za-z0-9_])(__[A-Za-z][A-Za-z0-9_]*)/gm)) {
        const name = m[1];
        if (name.startsWith('__gsharp') || builtins.test(name)) {
            continue;
        }
        const key = name.replace(/^(__GSHARP_STRUCT_).*/, '$1*');
        counts.set(key, (counts.get(key) || 0) + 1);
    }
}
[...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .forEach(([name, count]) => console.log(`${String(count).padStart(7)} ${name}`));
