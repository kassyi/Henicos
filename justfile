set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
export DiffEngine_Disabled := "true"

# Run normal tests excluding heavy stress and benchmark tests
test *args="":
    dotnet test --filter "Category!=Stress&Category!=Benchmark" {{args}}

# Run all tests (including stress tests)
test-all *args="":
    dotnet test {{args}}

# Run tests multiple times to detect flaky tests
test-stability count="10" continue_on_error="false" shuffle="true" seed="" *args="":
    $null = New-Item -ItemType Directory -Force -Path TestResults; \
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; \
    $OutputEncoding = [System.Text.Encoding]::UTF8; \
    Write-Host "Building project once before stability tests..." -ForegroundColor Yellow; \
    dotnet build; \
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE; } \
    $totalSw = [System.Diagnostics.Stopwatch]::StartNew(); \
    $hasFailure = $false; \
    for ($i = 1; $i -le {{count}}; $i++) { \
        if ('{{shuffle}}' -eq 'true') { \
            $currentSeed = if ('{{seed}}' -ne '') { [int]'{{seed}}' + $i - 1 } else { Get-Random }; \
            $env:TEST_SEED = "$currentSeed"; \
            $seedInfo = " (Seed: $currentSeed)"; \
        } else { \
            Remove-Item env:TEST_SEED -ErrorAction SilentlyContinue; \
            $seedInfo = ""; \
        } \
        Write-Host "[$i/{{count}}] Running test iteration$seedInfo..." -ForegroundColor Cyan; \
        $logHeader = if ('{{shuffle}}' -eq 'true') { "=== Stability Test Iteration $i (Seed: $currentSeed) ===" } else { "=== Stability Test Iteration $i (Shuffle: disabled) ===" }; \
        Set-Content -Path "TestResults/stability_iter_$i.log" -Value $logHeader -Encoding utf8; \
        $lapSw = [System.Diagnostics.Stopwatch]::StartNew(); \
        dotnet test --no-build --logger "trx;LogFileName=stability_iter_$i.trx" --filter "Category!=Stress&Category!=Benchmark" {{args}} 2>&1 | Out-File -FilePath TestResults/stability_iter_$i.log -Encoding utf8 -Append; \
        $exitCode = $LASTEXITCODE; \
        $lapSw.Stop(); \
        $lapElapsed = $lapSw.Elapsed; \
        $lapStr = "{0:mm}:{0:ss}.{0:fff} ({1}s)" -f $lapElapsed, [math]::Round($lapElapsed.TotalSeconds, 2); \
        if ($exitCode -ne 0) { \
            $hasFailure = $true; \
            Write-Host "Test failed on iteration $i$seedInfo after $lapStr! See TestResults/stability_iter_$i.log for details." -ForegroundColor Red; \
            if ('{{continue_on_error}}' -eq 'false') { \
                Remove-Item env:TEST_SEED -ErrorAction SilentlyContinue; \
                exit $exitCode; \
            } else { \
                Write-Host "Continuing to next iteration (--continue_on_error is set)..." -ForegroundColor Yellow; \
            } \
        } else { \
            Write-Host "Iteration $i$seedInfo passed in $lapStr" -ForegroundColor Green; \
        } \
    } \
    Remove-Item env:TEST_SEED -ErrorAction SilentlyContinue; \
    $totalSw.Stop(); \
    $totElapsed = $totalSw.Elapsed; \
    $totalStr = "{0:mm}:{0:ss}.{0:fff} ({1}s)" -f $totElapsed, [math]::Round($totElapsed.TotalSeconds, 2); \
    if ($hasFailure) { \
        Write-Host "Finished in $totalStr, but some iterations failed." -ForegroundColor Red; \
        exit 1; \
    } else { \
        Write-Host "All {{count}} iterations passed successfully in $totalStr." -ForegroundColor Green; \
    }

# Watch and run tests with debounce delay (requires watchexec)

watch delay="3s" *args="":
    watchexec --debounce {{delay}} -w src -w tests -e cs,csproj --restart --clear -- just test {{args}}

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

