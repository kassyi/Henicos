set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
export DiffEngine_Disabled := "true"

# Run normal tests excluding heavy stress tests
test *args="":
    dotnet test --filter "Category!=Stress" {{args}}

# Run all tests (including stress tests)
test-all *args="":
    dotnet test {{args}}

# Run syntax and code transpilation tests
test-syntax *args="":
    dotnet test --filter "Category=Syntax" {{args}}

# Run GPU kernel execution tests on real device
test-execution *args="":
    dotnet test --filter "Category=Execution" {{args}}

# Run concurrency and high-load stress tests
test-stress *args="":
    dotnet test --filter "Category=Stress" {{args}}

# Run sad path and diagnostic error detection tests
test-sadpath *args="":
    dotnet test --filter "Category=SadPath|Category=Diagnostics" {{args}}

# Run runtime, JIT engine, and transpiler infrastructure tests
test-runtime *args="":
    dotnet test --filter "Category=Runtime|Category=Transpiler" {{args}}

# Run unit tests (e.g. topological sorting)
test-unit *args="":
    dotnet test --filter "Category=Unit" {{args}}

# Run memory alignment and struct layout tests
test-alignment *args="":
    dotnet test --filter "Category=Alignment" {{args}}


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

# Launch the Interactive Roadmap UI
[working-directory('tools/roadmap-ui')]
roadmap:
    npm run dev

# Alias for roadmap
run-ui: roadmap
