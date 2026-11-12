set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
export DiffEngine_Disabled := "true"

# Every dotnet test of these recipes: a test host that dies (OutOfMemoryException, the heap hard limit of
# tests/Directory.Build.props) leaves a mini dump in its TestResults directory
blame_crash := "--blame-crash --blame-crash-dump-type mini"

# The MSBuild node limit (-m:<n>) of a job that the build queue (scripts/agent/build-queue.js) granted; empty otherwise
queue_msbuild_args := env_var_or_default("GSHARP_BUILD_QUEUE_MSBUILD_ARGS", "")

# Run normal tests excluding heavy stress and benchmark tests and the tests that raise a real GPU fault (GpuTrap)
test *args="":
    dotnet test --filter "Category!=Stress&Category!=Benchmark&Category!=GpuTrap" {{blame_crash}} {{queue_msbuild_args}} {{args}}

# Run tests with code coverage collection
test-coverage *args="":
    dotnet test -s coverage.runsettings --filter "Category!=Stress&Category!=Benchmark&Category!=GpuTrap" {{blame_crash}} {{queue_msbuild_args}} {{args}}

# Run the tests with coverage and list the lines changed since base that no test executed
coverage-diff base="main":
    node scripts/human/coverage-diff.js --run --base {{base}}

# Differential tests: C# oracle vs managed vs native CPU (no GPU)
test-differential-cpu *args="":
    dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu/Kassyi.GSharp.Tests.Cpu.csproj \
        --filter "Category=Differential&Category!=Stress" {{blame_crash}} {{queue_msbuild_args}} {{args}}

# Build Tests.Cpu with native sanitizers (ASan/UBSan on Linux, ASan on Windows) and run Differential + Execution tests
test-sanitized *args="":
    node scripts/human/test-sanitized.js {{args}}

# Differential tests: C# oracle vs CUDA (agents: node scripts/agent/build-queue.js run --gpu -- "...")
test-differential-cuda *args="":
    dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda/Kassyi.GSharp.Tests.Cuda.csproj \
        --filter "Category=Differential&Category!=Stress" {{blame_crash}} {{queue_msbuild_args}} {{args}}

# Tests that raise a real GPU fault (a CUDA trap of a cooperative launch or an mbarrier wait), each in a child process,
# one at a time pause_ms apart, plus the CUDA fault seeds of the differential tests that CUDA does not check (agents:
# node scripts/agent/build-queue.js run --gpu -- "...")
test-gpu-trap pause_ms="3000" *args="":
    dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda/Kassyi.GSharp.Tests.Cuda.csproj \
        --filter "(Category=GpuTrap|Category=Differential)&Category!=Stress" \
        -e GSHARP_CUDA_TRAP_TESTS=1 -e GSHARP_CUDA_TRAP_PAUSE_MS={{pause_ms}} {{blame_crash}} {{queue_msbuild_args}} {{args}}

# Run differential kernel fuzzer (profile: M|C|G or managed|cpu|all)
fuzz-diff profile="cpu" minutes="5" *args="":
    node scripts/human/fuzz-differential.js --profile {{profile}} --minutes {{minutes}} {{args}}

# Compare generated C++/CUDA of a base ref and the working tree on fuzzer kernels
fuzz-compare base="origin/main" batches="20" *args="":
    node scripts/human/fuzz-differential.js --compare-base {{base}} --batches {{batches}} {{args}}

# count can take the time: `test-stability 5h15m` for 5 hours 15 minutes, `test-stability 10000` for 10000 times
# Run tests multiple times to detect flaky tests; see scripts/human/test-stability.js --help
test-stability count="10" continue_on_error="false" shuffle="true" seed="" *args="":
    node scripts/human/test-stability.js --count "{{count}}" \
        {{ if continue_on_error == "true" { "--continue-on-error" } else { "" } }} \
        {{ if shuffle == "true" { "" } else { "--no-shuffle" } }} \
        {{ if seed == "" { "" } else { "--seed " + seed } }} \
        {{ if args == "" { "" } else { "-- " + args } }}

# Run the tests of one TestCategory (values: the TestCategory enum), e.g. `just test-category Stress`
test-category category *args="":
    dotnet test --filter "Category={{category}}" {{blame_crash}} {{queue_msbuild_args}} {{args}}

# e.g. `just test-memory Kassyi.GSharp.Tests.Cpu -c Release --filter "Category!=GpuExecution"`; test-memory.js --help
# One run of a test project: its memory peak, the tests that raised it, dotnet-counters and a gcdump at the end
test-memory project *args="":
    node scripts/human/test-memory.js {{project}} {{args}}

# Run compute benchmarks (sgemm, sha256, bs, reduce, all)
bench suite="all" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- compute-bench {{suite}} {{args}}

update-agents:
    npx --yes "git+ssh://git@github.com/kassyi/antigravity-global-agents.git" --copy

# Run GPU Pi Benchmark (e.g. `just bench-pi 100k`, `just bench-pi 1M`, `just bench-pi 1M --hex`)
bench-pi digits="100k" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- gpu-pi {{digits}} {{args}}

# Run a C# code snippet as a DocTest with explicitly defined dependencies
doctest file_path assemblies="":
    node scripts/human/test-doc-snippet.js --file-path "{{file_path}}" --assemblies "{{assemblies}}"

# e.g. `just mutate gpumath`, `just mutate compiler main`, `just mutate compiler "" -m "**/Layout/**/*.cs"`
# Mutation testing with Stryker.NET. phase: gpumath | compiler | compiler-cuda (the CUDA dialect snapshots only).
# since: a git ref to mutate only files changed after it (empty = all files). Reports go to tmp/stryker/<phase>.
# The test host heap is capped at 8 GB (GSharpTestHeapHardLimit in bytes; set it to override), so a mutant that
# allocates without bound fails instead of exhausting memory.
mutate phase="gpumath" since="" *args="":
    node scripts/human/mutate.js --phase "{{phase}}" {{ if since != "" { "--since " + since } else { "" } }} -- {{args}}

# Demangle C++ / G# identifiers in a build/runtime log (pass file path, "-" for stdin, or "help" / "-- --help" for usage)
gsharp-demangle log_path="-" symbols_path="":
    dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle "{{log_path}}" "{{symbols_path}}"

# Print the environment of a bug report (OS, GPU, CUDA, host C++, .NET) as Markdown; e.g. `just env-info --json`. Same source on Windows and Unix; needs the .NET SDK 10.0.300 or later (`#:include`)
env-info *args="":
    dotnet run tools/collect-env/collect-env.cs -- {{args}}

# Print a trusted_keys.txt line (`<key>,fnv1a64:<fingerprint>`) for a 64-char hex or 44-char Base64 Ed25519 public key
[windows]
trusted-key-line key:
    $k = '{{key}}'; if ($k.Length -eq 64) { $hex = $k.ToLowerInvariant() } elseif ($k.Length -eq 44) { $hex = [System.BitConverter]::ToString([Convert]::FromBase64String($k)).Replace('-', '').ToLowerInvariant() } else { throw 'key must be 64 hex or 44 Base64 characters' }; [bigint]$h = [bigint]::Parse('14695981039346656037'); [bigint]$prime = [bigint]::Parse('1099511628211'); [bigint]$mod = [bigint]::Pow(2, 64); foreach ($c in $hex.ToCharArray()) { $h = (($h -bxor [bigint][int]$c) * $prime) % $mod }; $s = $h.ToString('x').TrimStart('0').PadLeft(16, '0'); "$k,fnv1a64:" + $s.Substring(0, 10)

[unix]
trusted-key-line key:
    #!/usr/bin/env bash
    set -eu
    k='{{key}}'
    case ${#k} in
        64) hex=$(printf '%s' "$k" | tr 'A-F' 'a-f') ;;
        44) hex=$(printf '%s' "$k" | base64 -d | od -An -v -tx1 | tr -d '[:space:]') ;;
        *) echo 'key must be 64 hex or 44 Base64 characters' >&2; exit 1 ;;
    esac
    # bash integer arithmetic wraps modulo 2^64, which is exactly what 64-bit FNV-1a needs
    h=-3750763034362895579
    for ((i = 0; i < ${#hex}; i++)); do
        printf -v c '%d' "'${hex:i:1}"
        h=$(( (h ^ c) * 1099511628211 ))
    done
    printf '%s,fnv1a64:%s\n' "$k" "$(printf '%016x' "$h" | cut -c1-10)"


# Run every long check in one go, about a day (benchmarks, fuzzer, sanitized tests, test-stability, coverage, mutation); see scripts/human/long-checks.js --help
long-checks *args="":
    node scripts/human/long-checks.js {{args}}

# Compare the BenchmarkDotNet *-report-full.json reports of a base and a head run with a Mann-Whitney U test;
# exits 1 on a significant slowdown (options: --threshold <percent>, --pvalue <p>, --json-out <file>)
bench-compare base head *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- bench-compare {{base}} {{head}} {{args}}
