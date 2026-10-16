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
[windows]
test-stability count="10" continue_on_error="false" shuffle="true" seed="" *args="":
    $ts = Get-Date -Format 'yyyyMMdd_HHmmss'; \
    $resultDir = "TestResults/win/$ts"; \
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
        $timeLimitStr = $(if ($tsObj.Days -gt 0) { "{0}d {1:hh\:mm\:ss}" -f $tsObj.Days, $tsObj } else { "{0:hh\:mm\:ss}" -f $tsObj }); \
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
            $currentSeed = $(if ('{{seed}}' -ne '') { [int]'{{seed}}' + $i - 1 } else { Get-Random }); \
            $env:TEST_SEED = "$currentSeed"; \
            $seedInfo = " (Seed: $currentSeed)"; \
        } else { \
            Remove-Item env:TEST_SEED -ErrorAction SilentlyContinue; \
            $seedInfo = ""; \
        } \
        $iterProgress = $(if ($isTimeMode) { \
            $curElapsed = $totalSw.Elapsed; \
            "[$i] (Elapsed: {0:hh\:mm\:ss} / $timeLimitStr)" -f $curElapsed \
        } else { \
            "[$i/$maxCount]" \
        }); \
        Write-Host "$iterProgress Running test iteration$seedInfo..." -ForegroundColor Cyan; \
        $logHeader = $(if ('{{shuffle}}' -eq 'true') { "=== Stability Test Iteration $i (Seed: $currentSeed) ===" } else { "=== Stability Test Iteration $i (Shuffle: disabled) ===" }); \
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
                seed = $(if ('{{shuffle}}' -eq 'true') { "$currentSeed" } else { $null }); \
                elapsedSeconds = [math]::Round($lapElapsed.TotalSeconds, 2); \
                log = "stability_iter_$i.log"; \
            }; \
            $failedList += [pscustomobject]$failItem; \
            $interimSummary = [ordered]@{ \
                timestamp = $ts; \
                mode = $(if ($isTimeMode) { "duration" } else { "count" }); \
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
        mode = $(if ($isTimeMode) { "duration" } else { "count" }); \
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

[unix]
test-stability count="10" continue_on_error="false" shuffle="true" seed="" *args="":
    @set -e; \
    if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then \
        export DOTNET_ROOT="$HOME/.dotnet"; \
        export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"; \
    fi; \
    ts=$(date '+%Y%m%d_%H%M%S'); \
    resultDir="TestResults/linux/$ts"; \
    mkdir -p "$resultDir"; \
    targetInput=$(printf '%s' '{{count}}' | tr -d '[:space:]'); \
    isTimeMode=false; \
    timeLimitSec=0; \
    maxCount=0; \
    if printf '%s' "$targetInput" | grep -Eq '^[0-9]+$'; then \
        maxCount=$targetInput; \
        modeDesc="$maxCount iterations"; \
    else \
        isTimeMode=true; \
        h=$(printf '%s' "$targetInput" | sed -nE 's/^([0-9]+)h.*/\1/p; s/.*[^0-9]([0-9]+)h.*/\1/p'); \
        m=$(printf '%s' "$targetInput" | sed -nE 's/^([0-9]+)m.*/\1/p; s/.*[^0-9]([0-9]+)m.*/\1/p'); \
        s=$(printf '%s' "$targetInput" | sed -nE 's/^([0-9]+)s.*/\1/p; s/.*[^0-9]([0-9]+)s.*/\1/p'); \
        if [ -n "$h" ]; then timeLimitSec=$((timeLimitSec + h * 3600)); fi; \
        if [ -n "$m" ]; then timeLimitSec=$((timeLimitSec + m * 60)); fi; \
        if [ -n "$s" ]; then timeLimitSec=$((timeLimitSec + s)); fi; \
        if [ "$timeLimitSec" -le 0 ]; then \
            printf '\033[31mInvalid count/duration format: '\''%s'\''. Use numbers (e.g. 100) or duration (e.g. 5h30m, 1h, 45s).\033[0m\n' "$targetInput"; \
            exit 1; \
        fi; \
        timeLimitStr=$(awk -v sec="$timeLimitSec" 'BEGIN { d=int(sec/86400); r=sec%86400; h=int(r/3600); m=int((r%3600)/60); s=r%60; if (d>0) printf "%dd %02d:%02d:%02d", d, h, m, s; else printf "%02d:%02d:%02d", h, m, s }'); \
        modeDesc="Time-based ($targetInput / $timeLimitStr)"; \
    fi; \
    if [ -z "${GSHARP_TEST_NO_CUDA:-}" ] && ! command -v nvcc >/dev/null 2>&1; then \
        export GSHARP_TEST_NO_CUDA=1; \
    fi; \
    printf '\033[36mStability test mode: %s\033[0m\n' "$modeDesc"; \
    printf '\033[36mStability test output directory: %s\033[0m\n' "$resultDir"; \
    printf '\033[33mBuilding project once before stability tests...\033[0m\n'; \
    dotnet build {{args}}; \
    totalStartMs=$(date '+%s%3N'); \
    hasFailure=false; \
    failedCount=0; \
    failedJson=""; \
    i=1; \
    while :; do \
        nowMs=$(date '+%s%3N'); \
        elapsedSec=$(( (nowMs - totalStartMs) / 1000 )); \
        if [ "$isTimeMode" = "false" ] && [ "$i" -gt "$maxCount" ]; then break; fi; \
        if [ "$isTimeMode" = "true" ] && [ "$i" -gt 1 ] && [ "$elapsedSec" -ge "$timeLimitSec" ]; then break; fi; \
        if [ '{{shuffle}}' = 'true' ]; then \
            if [ -n '{{seed}}' ]; then \
                currentSeed=$(( {{seed}} + i - 1 )); \
            else \
                currentSeed=$(od -An -N4 -tu4 /dev/urandom | tr -d '[:space:]'); \
                currentSeed=$(( currentSeed % 2147483647 + 1 )); \
            fi; \
            export TEST_SEED="$currentSeed"; \
            seedInfo=" (Seed: $currentSeed)"; \
            logHeader="=== Stability Test Iteration $i (Seed: $currentSeed) ==="; \
        else \
            unset TEST_SEED; \
            currentSeed=""; \
            seedInfo=""; \
            logHeader="=== Stability Test Iteration $i (Shuffle: disabled) ==="; \
        fi; \
        if [ "$isTimeMode" = "true" ]; then \
            curElapsedStr=$(awk -v sec="$elapsedSec" 'BEGIN { d=int(sec/86400); r=sec%86400; h=int(r/3600); m=int((r%3600)/60); s=r%60; if (d>0) printf "%dd %02d:%02d:%02d", d, h, m, s; else printf "%02d:%02d:%02d", h, m, s }'); \
            iterProgress="[$i] (Elapsed: $curElapsedStr / $timeLimitStr)"; \
        else \
            iterProgress="[$i/$maxCount]"; \
        fi; \
        printf '\033[36m%s Running test iteration%s...\033[0m\n' "$iterProgress" "$seedInfo"; \
        iterLog="$resultDir/stability_iter_$i.log"; \
        printf '%s\n' "$logHeader" > "$iterLog"; \
        lapStartMs=$(date '+%s%3N'); \
        set +e; \
        if [ "${GSHARP_TEST_NO_CUDA:-}" = "1" ] && [ -z '{{args}}' ]; then \
            dotnet test --no-build --filter "Category!=Stress&Category!=Benchmark&Category!=GpuExecution" >> "$iterLog" 2>&1 \
            && dotnet test tests/Kassyi.GSharp.Tests.Cpu/Kassyi.GSharp.Tests.Cpu.csproj --no-build --filter "Category=GpuExecution&Category!=Stress&Category!=Benchmark" >> "$iterLog" 2>&1; \
            exitCode=$?; \
        else \
            dotnet test --no-build --filter "Category!=Stress&Category!=Benchmark" {{args}} >> "$iterLog" 2>&1; \
            exitCode=$?; \
        fi; \
        set -e; \
        lapEndMs=$(date '+%s%3N'); \
        lapMs=$(( lapEndMs - lapStartMs )); \
        lapStr=$(awk -v ms="$lapMs" 'BEGIN { sec=ms/1000.0; m=int(sec/60); s=int(sec)%60; rem=ms%1000; printf "%02d:%02d.%03d (%.2fs)", m, s, rem, sec }'); \
        lapSec=$(awk -v ms="$lapMs" 'BEGIN { printf "%.2f", ms/1000.0 }'); \
        totalElapsedSec=$(awk -v ms="$(( lapEndMs - totalStartMs ))" 'BEGIN { printf "%.2f", ms/1000.0 }'); \
        modeName=$(if [ "$isTimeMode" = "true" ]; then printf 'duration'; else printf 'count'; fi); \
        if [ "$exitCode" -ne 0 ]; then \
            hasFailure=true; \
            failedCount=$(( failedCount + 1 )); \
            seedJson=$(if [ '{{shuffle}}' = 'true' ]; then printf '"%s"' "$currentSeed"; else printf 'null'; fi); \
            itemJson=$(printf '{\n      "iteration": %d,\n      "seed": %s,\n      "elapsedSeconds": %s,\n      "log": "stability_iter_%d.log"\n    }' "$i" "$seedJson" "$lapSec" "$i"); \
            if [ -z "$failedJson" ]; then failedJson="$itemJson"; else failedJson=$(printf '%s,\n    %s' "$failedJson" "$itemJson"); fi; \
            passedCount=$(( i - failedCount )); \
            printf '{\n  "timestamp": "%s",\n  "mode": "%s",\n  "target": "%s",\n  "executed": %d,\n  "passed": %d,\n  "failed": %d,\n  "totalElapsedSeconds": %s,\n  "failedIterations": [\n    %s\n  ]\n}\n' \
                "$ts" "$modeName" "$targetInput" "$i" "$passedCount" "$failedCount" "$totalElapsedSec" "$failedJson" > "$resultDir/summary.json"; \
            printf '\033[31mTest failed on iteration %d%s after %s! See %s for details.\033[0m\n' "$i" "$seedInfo" "$lapStr" "$iterLog"; \
            if [ '{{continue_on_error}}' = 'false' ]; then \
                unset TEST_SEED; \
                exit "$exitCode"; \
            else \
                printf '\033[33mContinuing to next iteration (--continue_on_error is set)...\033[0m\n'; \
            fi; \
        else \
            printf '\033[32mIteration %d%s passed in %s\033[0m\n' "$i" "$seedInfo" "$lapStr"; \
        fi; \
        i=$(( i + 1 )); \
    done; \
    unset TEST_SEED; \
    totMs=$(( $(date '+%s%3N') - totalStartMs )); \
    totalStr=$(awk -v ms="$totMs" 'BEGIN { sec=ms/1000.0; m=int(sec/60); s=int(sec)%60; rem=ms%1000; printf "%02d:%02d.%03d (%.2fs)", m, s, rem, sec }'); \
    totalElapsedSec=$(awk -v ms="$totMs" 'BEGIN { printf "%.2f", ms/1000.0 }'); \
    executedCount=$(( i - 1 )); \
    passedCount=$(( executedCount - failedCount )); \
    modeName=$(if [ "$isTimeMode" = "true" ]; then printf 'duration'; else printf 'count'; fi); \
    if [ -z "$failedJson" ]; then \
        printf '{\n  "timestamp": "%s",\n  "mode": "%s",\n  "target": "%s",\n  "executed": %d,\n  "passed": %d,\n  "failed": %d,\n  "totalElapsedSeconds": %s,\n  "failedIterations": []\n}\n' \
            "$ts" "$modeName" "$targetInput" "$executedCount" "$passedCount" "$failedCount" "$totalElapsedSec" > "$resultDir/summary.json"; \
    else \
        printf '{\n  "timestamp": "%s",\n  "mode": "%s",\n  "target": "%s",\n  "executed": %d,\n  "passed": %d,\n  "failed": %d,\n  "totalElapsedSeconds": %s,\n  "failedIterations": [\n    %s\n  ]\n}\n' \
            "$ts" "$modeName" "$targetInput" "$executedCount" "$passedCount" "$failedCount" "$totalElapsedSec" "$failedJson" > "$resultDir/summary.json"; \
    fi; \
    find "$resultDir" -mindepth 1 -type d -empty -delete 2>/dev/null || true; \
    if [ "$hasFailure" = "true" ]; then \
        printf '\033[31mFinished in %s (%d iterations), but some iterations failed. See %s for logs & summary.json.\033[0m\n' "$totalStr" "$executedCount" "$resultDir"; \
        exit 1; \
    else \
        printf '\033[32mAll %d iterations passed successfully in %s. Results saved to %s.\033[0m\n' "$executedCount" "$totalStr" "$resultDir"; \
    fi

# Run stability tests in WSL (Linux) directly from Windows
[windows]
test-stability-wsl count="10" continue_on_error="false" shuffle="true" seed="" *args="":
    wsl -- bash -lc "just test-stability '{{count}}' '{{continue_on_error}}' '{{shuffle}}' '{{seed}}' {{args}}"

# Run stability tests sequentially on Windows then WSL (recommended to avoid CPU/GPU/IO contention)
[windows]
test-stability-both count="10" continue_on_error="false" shuffle="true" seed="" *args="":
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; \
    Write-Host "=== [1/2] Running stability tests on Windows ===" -ForegroundColor Magenta; \
    just test-stability '{{count}}' '{{continue_on_error}}' '{{shuffle}}' '{{seed}}' {{args}}; \
    $winExit = $LASTEXITCODE; \
    if ($winExit -ne 0 -and '{{continue_on_error}}' -eq 'false') { exit $winExit; } \
    Write-Host "=== [2/2] Running stability tests on WSL (Linux) ===" -ForegroundColor Magenta; \
    just test-stability-wsl '{{count}}' '{{continue_on_error}}' '{{shuffle}}' '{{seed}}' {{args}}; \
    $wslExit = $LASTEXITCODE; \
    if ($winExit -ne 0) { exit $winExit; } \
    exit $wslExit


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
    cmd /c ".\src\Native\Kassyi.GSharp.Stdlib.GpuMath.Cuda\build.bat"
build-cs:
    dotnet build

format:
    dotnet format Henicos.slnx

# Launch the Interactive Roadmap UI
roadmap:
    npm --prefix tools/roadmap-ui run dev

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

