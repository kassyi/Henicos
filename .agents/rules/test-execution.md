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

Pick the command matching the domain you modified. Methodology (TDD, snapshot hierarchy) lives in [test-architecture.md](test-architecture.md).
