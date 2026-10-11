set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
export DiffEngine_Disabled := "true"

# Run normal tests excluding heavy stress and benchmark tests
test *args="":
    dotnet test --filter "Category!=Stress&Category!=Benchmark" {{args}}

# Run all tests (including stress tests)
test-all *args="":
    dotnet test {{args}}

# Run tests with code coverage collection
test-coverage *args="":
    dotnet test -s coverage.runsettings --filter "Category!=Stress&Category!=Benchmark" {{args}}

# Run tests multiple times to detect flaky tests
# count can take the time: 
#	test-stability 5h15m # for 5 hours 15 minutes
#	test-stability 10000 # for 10000 times
test-stability count="10" continue_on_error="false" shuffle="true" seed="" *args="":
    $ts = Get-Date -Format 'yyyyMMdd_HHmmss'; \
    $resultDir = "TestResults/$ts"; \
    $null = New-Item -ItemType Directory -Force -Path $resultDir; \
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; \
    $OutputEncoding = [System.Text.Encoding]::UTF8; \
    $targetInput = '{{count}}'.Trim(); \
    $isTimeMode = $false; \
    $timeLimitSec = 0; \
    $maxCount = 0; \
    if ($targetInput -match '^\d+$') { \
        $maxCount = [int]$targetInput; \
        $modeDesc = "$maxCount iterations"; \
    } else { \
        $isTimeMode = $true; \
        if ($targetInput -match '(\d+)\s*h') { $timeLimitSec += [int]$matches[1] * 3600; } \
        if ($targetInput -match '(\d+)\s*m') { $timeLimitSec += [int]$matches[1] * 60; } \
        if ($targetInput -match '(\d+)\s*s') { $timeLimitSec += [int]$matches[1]; } \
        if ($timeLimitSec -le 0) { \
            Write-Host "Invalid count/duration format: '$targetInput'. Use numbers (e.g. 100) or duration (e.g. 5h30m, 1h, 45s)." -ForegroundColor Red; \
            exit 1; \
        } \
        $tsObj = [TimeSpan]::FromSeconds($timeLimitSec); \
        $timeLimitStr = if ($tsObj.Days -gt 0) { "{0}d {1:hh\:mm\:ss}" -f $tsObj.Days, $tsObj } else { "{0:hh\:mm\:ss}" -f $tsObj }; \
        $modeDesc = "Time-based ($targetInput / $timeLimitStr)"; \
    } \
    Write-Host "Stability test mode: $modeDesc" -ForegroundColor Cyan; \
    Write-Host "Stability test output directory: $resultDir" -ForegroundColor Cyan; \
    Write-Host "Building project once before stability tests..." -ForegroundColor Yellow; \
    dotnet build; \
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE; } \
    $totalSw = [System.Diagnostics.Stopwatch]::StartNew(); \
    $hasFailure = $false; \
    $failedList = @(); \
    $i = 1; \
    while ($true) { \
        if (-not $isTimeMode -and $i -gt $maxCount) { break; } \
        if ($isTimeMode -and $i -gt 1 -and $totalSw.Elapsed.TotalSeconds -ge $timeLimitSec) { break; } \
        if ('{{shuffle}}' -eq 'true') { \
            $currentSeed = if ('{{seed}}' -ne '') { [int]'{{seed}}' + $i - 1 } else { Get-Random }; \
            $env:TEST_SEED = "$currentSeed"; \
            $seedInfo = " (Seed: $currentSeed)"; \
        } else { \
            Remove-Item env:TEST_SEED -ErrorAction SilentlyContinue; \
            $seedInfo = ""; \
        } \
        $iterProgress = if ($isTimeMode) { \
            $curElapsed = $totalSw.Elapsed; \
            "[$i] (Elapsed: {0:hh\:mm\:ss} / $timeLimitStr)" -f $curElapsed \
        } else { \
            "[$i/$maxCount]" \
        }; \
        Write-Host "$iterProgress Running test iteration$seedInfo..." -ForegroundColor Cyan; \
        $logHeader = if ('{{shuffle}}' -eq 'true') { "=== Stability Test Iteration $i (Seed: $currentSeed) ===" } else { "=== Stability Test Iteration $i (Shuffle: disabled) ===" }; \
        $iterLog = "$resultDir/stability_iter_$i.log"; \
        Set-Content -Path $iterLog -Value $logHeader -Encoding utf8; \
        $lapSw = [System.Diagnostics.Stopwatch]::StartNew(); \
        dotnet test --no-build --filter "Category!=Stress&Category!=Benchmark" {{args}} 2>&1 | Out-File -FilePath $iterLog -Encoding utf8 -Append; \
        $exitCode = $LASTEXITCODE; \
        $lapSw.Stop(); \
        $lapElapsed = $lapSw.Elapsed; \
        $lapStr = "{0:mm}:{0:ss}.{0:fff} ({1}s)" -f $lapElapsed, [math]::Round($lapElapsed.TotalSeconds, 2); \
        if ($exitCode -ne 0) { \
            $hasFailure = $true; \
            $failItem = [ordered]@{ \
                iteration = $i; \
                seed = (if ('{{shuffle}}' -eq 'true') { "$currentSeed" } else { $null }); \
                elapsedSeconds = [math]::Round($lapElapsed.TotalSeconds, 2); \
                log = "stability_iter_$i.log"; \
            }; \
            $failedList += [pscustomobject]$failItem; \
            $interimSummary = [ordered]@{ \
                timestamp = $ts; \
                mode = (if ($isTimeMode) { "duration" } else { "count" }); \
                target = $targetInput; \
                executed = $i; \
                passed = $i - $failedList.Count; \
                failed = $failedList.Count; \
                totalElapsedSeconds = [math]::Round($totalSw.Elapsed.TotalSeconds, 2); \
                failedIterations = $failedList; \
            }; \
            $interimSummary | ConvertTo-Json -Depth 5 | Set-Content -Path "$resultDir/summary.json" -Encoding utf8; \
            Write-Host "Test failed on iteration $i$seedInfo after $lapStr! See $iterLog for details." -ForegroundColor Red; \
            if ('{{continue_on_error}}' -eq 'false') { \
                Remove-Item env:TEST_SEED -ErrorAction SilentlyContinue; \
                exit $exitCode; \
            } else { \
                Write-Host "Continuing to next iteration (--continue_on_error is set)..." -ForegroundColor Yellow; \
            } \
        } else { \
            Write-Host "Iteration $i$seedInfo passed in $lapStr" -ForegroundColor Green; \
        } \
        $i++; \
    } \
    Remove-Item env:TEST_SEED -ErrorAction SilentlyContinue; \
    $totalSw.Stop(); \
    $totElapsed = $totalSw.Elapsed; \
    $totalStr = "{0:mm}:{0:ss}.{0:fff} ({1}s)" -f $totElapsed, [math]::Round($totElapsed.TotalSeconds, 2); \
    $executedCount = $i - 1; \
    $summary = [ordered]@{ \
        timestamp = $ts; \
        mode = (if ($isTimeMode) { "duration" } else { "count" }); \
        target = $targetInput; \
        executed = $executedCount; \
        passed = $executedCount - $failedList.Count; \
        failed = $failedList.Count; \
        totalElapsedSeconds = [math]::Round($totElapsed.TotalSeconds, 2); \
        failedIterations = $failedList; \
    }; \
    Get-ChildItem -Path $resultDir -Directory -ErrorAction SilentlyContinue | Where-Object { (Get-ChildItem -Path $_.FullName -Recurse | Measure-Object).Count -eq 0 } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; \
    $summary | ConvertTo-Json -Depth 5 | Set-Content -Path "$resultDir/summary.json" -Encoding utf8; \
    if ($hasFailure) { \
        Write-Host "Finished in $totalStr ($executedCount iterations), but some iterations failed. See $resultDir for logs & summary.json." -ForegroundColor Red; \
        exit 1; \
    } else { \
        Write-Host "All $executedCount iterations passed successfully in $totalStr. Results saved to $resultDir." -ForegroundColor Green; \
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

