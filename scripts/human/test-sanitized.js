#!/usr/bin/env node
// Builds Kassyi.GSharp.Tests.Cpu with native sanitizers (ASan + UBSan on Linux/Unix, ASan on Windows),
// runs the Differential and Execution test categories, and demangles any sanitizer report via LogDemangler.
// The sanitized build goes to bin/<os>-sanitized and obj/<os>-sanitized (GSharpOutputVariant in Directory.Build.props),
// so a normal build or `dotnet test --no-build` afterwards still uses the uninstrumented native libraries.
// Test output is streamed to the console and to tmp/test-sanitized.log.
//
// Usage:
//   node scripts/human/test-sanitized.js [--sanitizers <list>] [--no-build] [--help] [<dotnet-test-args>...]
//   just test-sanitized [<dotnet-test-args>...]
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const repoRoot = path.resolve(__dirname, '..', '..');
const testProject = path.join('tests', 'Compiler', 'Kassyi.GSharp.Tests.Cpu', 'Kassyi.GSharp.Tests.Cpu.csproj');
const outputVariant = 'sanitized';
// Same names as GSharpHostOs in Directory.Build.props.
const hostOs = { win32: 'win', linux: 'linux', darwin: 'osx' }[process.platform] || 'unix';
const symbolsDir = path.join('tests', 'Compiler', 'Kassyi.GSharp.Tests.Cpu', 'obj', `${hostOs}-${outputVariant}`);
const generatorProject = path.join('src', 'Compiler', 'Kassyi.GSharp.Compiler.Generators');
const defaultFilter = '(Category=Differential|Category=Execution|Category=GpuExecution|Category=CpuExecution)&Category!=Stress&Category!=Benchmark';

// Only match actual AddressSanitizer / UndefinedBehaviorSanitizer report markers.
// Do not match `__gsharp_`, which can appear in ordinary managed/assertion failure messages.
const SANITIZER_REPORT_PATTERN = /(?:ERROR: AddressSanitizer|runtime error:|SUMMARY: UndefinedBehaviorSanitizer)/;

function printHelp() {
    console.log(
        [
            'Usage: node scripts/human/test-sanitized.js [options] [<dotnet-test-args>...]',
            '',
            'Builds Kassyi.GSharp.Tests.Cpu with GSharpNativeSanitizers enabled, runs the Differential',
            'and Execution test suites, and pipes output through the G# demangler if ASan or UBSan triggers.',
            'The build goes to bin/<os>-sanitized and obj/<os>-sanitized; output is also written to tmp/test-sanitized.log.',
            '',
            'Options:',
            '  --sanitizers <list>  Sanitizer list passed to GSharpNativeSanitizers',
            '                       (default: "address,undefined" on Unix, "address" on Windows).',
            '  --no-build           Skip the dotnet build step before running dotnet test.',
            '  --help, -h           Show this help message.',
            '',
            'Any additional arguments (such as --filter "...") are forwarded to `dotnet test`.'
        ].join('\n')
    );
}

function buildEnv() {
    const env = { ...process.env };
    env.DiffEngine_Disabled = 'true';
    env.CI = 'true';

    if (process.platform !== 'win32') {
        const check = spawnSync('dotnet', ['--version'], { env, encoding: 'utf8' });
        if (check.error && check.error.code === 'ENOENT') {
            const homeDotnet = path.join(os.homedir(), '.dotnet');
            const dotnetBin = path.join(homeDotnet, 'dotnet');
            if (fs.existsSync(dotnetBin)) {
                env.DOTNET_ROOT = homeDotnet;
                env.PATH = `${homeDotnet}${path.delimiter}${path.join(homeDotnet, 'tools')}${path.delimiter}${env.PATH || ''}`;
            }
        }
    }

    return env;
}

function parseArgs(argv) {
    let sanitizers = process.env.GSharpNativeSanitizers || (process.platform === 'win32' ? 'address' : 'address,undefined');
    let noBuild = false;
    const testArgs = [];

    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === '--help' || arg === '-h') {
            printHelp();
            process.exit(0);
        } else if (arg === '--sanitizers') {
            if (i + 1 >= argv.length) {
                console.error('Error: --sanitizers requires a value.');
                process.exit(1);
            }
            sanitizers = argv[++i];
        } else if (arg.startsWith('--sanitizers=')) {
            sanitizers = arg.slice('--sanitizers='.length);
        } else if (arg === '--no-build') {
            noBuild = true;
        } else {
            testArgs.push(arg);
        }
    }

    return { sanitizers, noBuild, testArgs };
}

function hasCustomFilter(args) {
    return args.some(arg => arg === '--filter' || arg.startsWith('--filter='));
}

function resolveAsanPreload(env) {
    const cxx = env.CXX || 'g++';
    const res = spawnSync(cxx, ['-print-file-name=libasan.so'], {
        cwd: repoRoot,
        env,
        encoding: 'utf8'
    });
    if (res.status !== 0 || !res.stdout) {
        // The compiler comes from the environment (CXX); keep its value out of the log (CodeQL js/clear-text-logging).
        console.error('Error: Failed to locate libasan.so with "-print-file-name=libasan.so" (the C++ compiler is CXX, default g++).');
        process.exit(1);
    }
    return res.stdout.trim();
}

// Runs `dotnet <args>` with its stdout and stderr written to the console and to logPath as they arrive.
function runTeed(args, env, logPath) {
    return new Promise(resolve => {
        const log = fs.createWriteStream(logPath, { encoding: 'utf8' });
        const child = spawn('dotnet', args, { cwd: repoRoot, env, stdio: ['inherit', 'pipe', 'pipe'] });
        child.stdout.on('data', chunk => {
            process.stdout.write(chunk);
            log.write(chunk);
        });
        child.stderr.on('data', chunk => {
            process.stderr.write(chunk);
            log.write(chunk);
        });
        child.on('error', err => {
            console.error(`Error: Failed to start dotnet test: ${err.message}`);
            log.end(() => resolve(1));
        });
        child.on('close', code => {
            log.end(() => resolve(code ?? 1));
        });
    });
}

async function main() {
    const { sanitizers, noBuild, testArgs } = parseArgs(process.argv.slice(2));
    const env = buildEnv();

    const tmpDir = path.join(repoRoot, 'tmp');
    fs.mkdirSync(tmpDir, { recursive: true });
    const logPath = path.join(tmpDir, 'test-sanitized.log');

    if (!noBuild) {
        // Escape ';' and ',' so MSBuild CLI does not split `-p:GSharpNativeSanitizers=address;undefined` into two properties.
        const msbuildSanitizers = sanitizers.replace(/;/g, '%3B').replace(/,/g, '%2C');
        const buildArgs = [
            'build',
            testProject,
            `-p:GSharpNativeSanitizers=${msbuildSanitizers}`,
            `-p:GSharpOutputVariant=${outputVariant}`,
            '-nodeReuse:false'
        ];

        if (process.platform !== 'win32') {
            // In a Git worktree created on Windows (where `.git` contains `gitdir: P:/...`), Linux
            // Microsoft.Build.Tasks.Git cannot resolve the Windows drive path and falls back to the parent
            // repository, treating files under `tmp/worktree/...` as untracked. With the SDK default
            // `EmbedUntrackedSources=true`, csc attempts to embed `#line 200 "CustomKernelFile.cu.cs"` from
            // `MicroModifiersTests.cs:66` into the Portable PDB and fails with CS1504. Disabling
            // `EmbedUntrackedSources` avoids CS1504 while keeping full Portable PDB debug symbols.
            buildArgs.push('-p:EmbedUntrackedSources=false');
        }

        const buildRes = spawnSync('dotnet', buildArgs, {
            cwd: repoRoot,
            env,
            stdio: 'inherit'
        });
        if (buildRes.status !== 0) {
            process.exit(buildRes.status ?? 1);
        }
    }

    const testEnv = { ...env };
    if (process.platform === 'win32') {
        if (!testEnv.ASAN_OPTIONS) {
            testEnv.ASAN_OPTIONS = 'halt_on_error=1:abort_on_error=0';
        }
    } else {
        const asanLib = resolveAsanPreload(env);
        testEnv.LD_PRELOAD = testEnv.LD_PRELOAD ? `${asanLib}:${testEnv.LD_PRELOAD}` : asanLib;
        if (!testEnv.ASAN_OPTIONS) {
            testEnv.ASAN_OPTIONS = 'detect_leaks=0:halt_on_error=1:abort_on_error=0';
        }
        if (!testEnv.UBSAN_OPTIONS) {
            testEnv.UBSAN_OPTIONS = 'print_stacktrace=1:halt_on_error=1';
        }
    }

    const dotnetTestArgs = ['test', testProject, '--no-build', `-p:GSharpOutputVariant=${outputVariant}`];
    if (!hasCustomFilter(testArgs)) {
        dotnetTestArgs.push('--filter', defaultFilter);
    }
    dotnetTestArgs.push(...testArgs);

    let exitCode = await runTeed(dotnetTestArgs, testEnv, logPath);

    const combinedOutput = fs.readFileSync(logPath, 'utf8');
    if (SANITIZER_REPORT_PATTERN.test(combinedOutput)) {
        // The raw output is already on the console; print the report again with C# names.
        console.log(`\nSanitizer report found; demangled log (${path.relative(repoRoot, logPath)}):`);
        const demangleRes = spawnSync(
            'dotnet',
            ['run', '--project', generatorProject, '--no-build', '--', 'demangle', logPath, symbolsDir],
            {
                cwd: repoRoot,
                env,
                stdio: 'inherit'
            }
        );
        if (demangleRes.status !== 0) {
            console.error('Error: The demangler failed; the log above keeps the mangled names.');
        }
        if (exitCode === 0) {
            exitCode = 1;
        }
    }

    process.exit(exitCode);
}

main().catch(err => {
    console.error(err);
    process.exit(1);
});
