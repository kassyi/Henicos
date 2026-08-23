set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]

run-benchmark:
    dotnet run --project src/Henicos -c Release -- --benchmark-gpu

update-agents:
    npx --yes "git+ssh://git@github.com/kassyi/antigravity-global-agents.git" --copy

build-cuda:
    cmd /c ".\src\Kassyi.GSharp.GpuBigInt.Cuda\build.bat"
build-cs:
    dotnet build