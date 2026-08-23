set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]

run-bench:
    dotnet run --project src/Henicos -c Release -- bench

update-agents:
    npx --yes "git+ssh://git@github.com/kassyi/antigravity-global-agents.git" --copy

build-cuda:
    cmd /c ".\src\Kassyi.GSharp.GpuBigInt.Cuda\build.bat"
build-cs:
    dotnet build

format:
    dotnet format Henicos.slnx