# Test Execution Rules (`just`)

> [!IMPORTANT]
> **Always run tests via `just` (or set `DiffEngine_Disabled=true` / `CI=true`).**
> `Verify` snapshot failures launch interactive diff viewers (DiffEngine) by default, which blocks headless/agent runs indefinitely. `just` disables this automatically.

| Command | Scope |
| ----- | ----- |
| `just test` | All normal tests (excluding Stress) |
| `just test-syntax` | Syntax parsing / C# transpilation (`Category=Syntax`) |
| `just test-execution` | Real GPU kernel execution (`Category=Execution`) |
| `just test-sadpath` | Diagnostics / error detection (`Category=SadPath\|Diagnostics`) |
| `just test-stress` | High-load / concurrency (`Category=Stress`) |
| `just test-runtime` | JIT engine / transpiler infrastructure (`Category=Runtime\|Transpiler`) |
| `just test-unit` | General unit tests (`Category=Unit`) |
| `just test-alignment` | Memory alignment / struct layout (`Category=Alignment`) |
| `just test-all` | Everything |
| `just coverage-diff [base]` | `just test` with coverage, then the lines changed since `base` (default `main`) that no test executed |
| `just mutate [gpumath\|compiler\|compiler-cuda] [since]` | Stryker.NET mutation test of the host code (`gpumath`: `Kassyi.GSharp.Stdlib.GpuMath`, `compiler`: `Kassyi.GSharp.Compiler` with `Tests.Cpu`, `compiler-cuda`: the same compiler with the `Tests.Cuda` snapshots, whose build takes about an hour), optionally only the files changed after `since`. Runs for hours and uses the GPU (`gpumath`); report in `tmp/stryker/<phase>`. The recipe deletes the AOT artifacts under `obj/` before and after the run (Stryker itself does not write them, see generator-invariants §7); the next build regenerates them |

Test assemblies discard console output (`TestConsole.Silence()`); set `GSHARP_TEST_VERBOSE=1` to see `Logger` / benchmark output.

Pick the command matching the domain you modified. Methodology (TDD, snapshot hierarchy) lives in [test-architecture.md](test-architecture.md).
