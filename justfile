set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
export DiffEngine_Disabled := "true"

# Run normal tests excluding heavy stress and benchmark tests
test *args="":
    dotnet test -m:1 --filter "Category!=Stress&Category!=Benchmark" {{args}}

# Run all tests (including stress tests)
test-all *args="":
    dotnet test -m:1 {{args}}

# Run tests multiple times to detect flaky tests
test-stability count="10" *args="":
    $null = New-Item -ItemType Directory -Force -Path TestResults; \
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; \
    $OutputEncoding = [System.Text.Encoding]::UTF8; \
    $totalSw = [System.Diagnostics.Stopwatch]::StartNew(); \
    for ($i = 1; $i -le {{count}}; $i++) { \
        Write-Host "[$i/{{count}}] Running test iteration..." -ForegroundColor Cyan; \
        $lapSw = [System.Diagnostics.Stopwatch]::StartNew(); \
        dotnet test -m:1 --logger "trx;LogFileName=stability_iter_$i.trx" --filter "Category!=Stress&Category!=Benchmark" {{args}} 2>&1 | Out-File -FilePath TestResults/stability_iter_$i.log -Encoding utf8; \
        $exitCode = $LASTEXITCODE; \
        $lapSw.Stop(); \
        $lapElapsed = $lapSw.Elapsed; \
        $lapStr = "{0:mm}:{0:ss}.{0:fff} ({1}s)" -f $lapElapsed, [math]::Round($lapElapsed.TotalSeconds, 2); \
        if ($exitCode -ne 0) { \
            Write-Host "Test failed on iteration $i after $lapStr! See TestResults/stability_iter_$i.log for details." -ForegroundColor Red; \
            exit $exitCode; \
        } \
        Write-Host "Iteration $i passed in $lapStr" -ForegroundColor Green; \
    } \
    $totalSw.Stop(); \
    $totElapsed = $totalSw.Elapsed; \
    $totalStr = "{0:mm}:{0:ss}.{0:fff} ({1}s)" -f $totElapsed, [math]::Round($totElapsed.TotalSeconds, 2); \
    Write-Host "All {{count}} iterations passed successfully in $totalStr." -ForegroundColor Green

# Watch and run tests with debounce delay (requires watchexec)

watch delay="3s" *args="":
    watchexec --debounce {{delay}} -w src -w tests -e cs,csproj --restart --clear -- just test {{args}}

# Run syntax and code transpilation tests
test-syntax *args="":
    dotnet test -m:1 --filter "Category=Syntax" {{args}}

# Run GPU kernel execution tests on real device
test-execution *args="":
    dotnet test -m:1 --filter "Category=GpuExecution" {{args}}

# Alias for test-execution
test-gpu *args="":
    dotnet test -m:1 --filter "Category=GpuExecution" {{args}}

# Run concurrency and high-load stress tests
test-stress *args="":
    dotnet test -m:1 --filter "Category=Stress" {{args}}

# Run compute benchmark integration and verification tests
test-bench *args="":
    dotnet test -m:1 --filter "Category=Benchmark" {{args}}

# Run sad path and diagnostic error detection tests
test-sadpath *args="":
    dotnet test -m:1 --filter "Category=SadPath|Category=Diagnostics" {{args}}

# Run runtime, JIT engine, and transpiler infrastructure tests
test-runtime *args="":
    dotnet test -m:1 --filter "Category=Runtime|Category=Transpiler" {{args}}

# Run unit tests (e.g. topological sorting)
test-unit *args="":
    dotnet test -m:1 --filter "Category=Unit" {{args}}

# Run memory alignment and struct layout tests
test-alignment *args="":
    dotnet test -m:1 --filter "Category=Alignment" {{args}}


# Run compute benchmarks (sgemm, sha256, bs, reduce, all)
run-bench suite="all" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- compute-bench {{suite}} {{args}}

# Alias for run-bench
bench suite="all" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- compute-bench {{suite}} {{args}}

update-agents:
    npx --yes "git+ssh://git@github.com/kassyi/antigravity-global-agents.git" --copy

build-cuda:
    cmd /c ".\src\Native\Kassyi.GSharp.Stdlib.Math.Cuda\build.bat"
build-cs:
    dotnet build

format:
    dotnet format Henicos.slnx

# Launch the Interactive Roadmap UI
[working-directory('tools/roadmap-ui')]
roadmap:
    npm run dev

# Alias for roadmap
run-ui: roadmap

# Run GPU Pi Benchmark (e.g. `just bench-pi 100k`, `just bench-pi 1M`, `just bench-pi 1M --hex`)
bench-pi digits="100k" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- gpu-pi {{digits}} {{args}}

# Short alias for GPU Pi Benchmark (e.g. `just pi 1M`, `just pi 100k`)
pi digits="100k" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- gpu-pi {{digits}} {{args}}

# Run GPU Pi Benchmark skipping decimal base conversion
bench-pi-hex digits="100k" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- gpu-pi {{digits}} --hex {{args}}

# Run CPU baseline Pi Benchmark (e.g. `just bench-pi-cpu 10k`)
bench-pi-cpu digits="10k" *args="":
    dotnet run --project src/Apps/Henicos.Benchmarks -c Release -- {{digits}} {{args}}


# Run a C# code snippet as a DocTest with explicitly defined dependencies
doctest file_path assemblies="":
    pwsh ./scripts/Test-DocSnippet.ps1 -FilePath {{file_path}} -Assemblies {{assemblies}}

