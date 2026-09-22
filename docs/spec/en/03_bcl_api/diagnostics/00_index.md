---
title: "Transpiler and Analyzer Diagnostics (GSHxxxx)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Comprehensive catalog of compiler errors, warnings, and analyzer rules emitted by G#."
---

# Transpiler and Analyzer Diagnostics (GSHxxxx)

The G# compiler and Roslyn analyzers enforce GPU safety and memory management rules at compile-time. When a constraint is violated, the build system reports one of the following diagnostic codes.

## Diagnostic Codes

| ID | Severity | Title |
| :--- | :--- | :--- |
| [GSH0001](GSH0001.md) | Error | Object creation not supported in GPU kernels |
| [GSH0002](GSH0002.md) | Error | String usage not supported in GPU kernels |
| [GSH0003](GSH0003.md) | Error | Unsupported syntax in GPU kernel |
| [GSH0004](GSH0004.md) | Error | GpuKernel return type must be void or blittable |
| [GSH0005](GSH0005.md) | Error | GpuBigIntExpr must be assigned via .Value |
| [GSH0006](GSH0006.md) | Error | InlinePtx argument must be a string literal |
| [GSH0007](GSH0007.md) | Error | Foreach is not allowed in GPU kernels |
| [GSH0008](GSH0008.md) | Error | LINQ is not allowed in GPU kernels |
| [GSH0009](GSH0009.md) | Error | Implicit var with literal needs suffix |
| [GSH0010](GSH0010.md) | Error | GSharp Prelude missing |
| [GSH0011](GSH0011.md) | Error | Macro lambda requires a parameter |
| [GSH0012](GSH0012.md) | Error | Macro arguments must be compile-time constants |
| [GSH0013](GSH0013.md) | Warning | Excessive static shared memory usage |
| [GSH0014](GSH0014.md) | Warning | Large struct passed by value |
| [GSH0015](GSH0015.md) | Warning | Auto-allocated array in loop |
| [GSH0016](GSH0016.md) | Error | Unroll factor must be a non-negative compile-time constant |
| [GSH0017](GSH0017.md) | Error | Unroll hint must precede a loop |
| [GSH0018](GSH0018.md) | Error | GPU intrinsic called from host |
| [GSH0019](GSH0019.md) | Error | Manual GetEnumerator calls are not supported |
| [GSH0020](GSH0020.md) | Error | Missing IDisposable implementation |
| [GSH0021](GSH0021.md) | Error | Undisposed GPU resource |
| [GSH0022](GSH0022.md) | Error | Unsafe field overwrite |
| [GSH0023](GSH0023.md) | Error | Property getter side-effect |
| [GSH0024](GSH0024.md) | Error | Cast operator side-effect |
| [GSH0025](GSH0025.md) | Error | Unguarded multi-allocation |
| [GSH0026](GSH0026.md) | Error | Static disposable allocation |
| [GSH0027](GSH0027.md) | Error | Disposing borrowed resource |
| [GSH0028](GSH0028.md) | Error | Disposable resource in Default Interface Method |
| [GSH0029](GSH0029.md) | Warning | Manual unmanaged allocation detected |
| [RS0030](RS0030.md) | Error | GPU intrinsic invoked from host code or banned API used in kernel |

## See Also

- [04_diagnostics_and_exceptions.md](../04_diagnostics_and_exceptions.md)
- [05_limitations.md](../../01_language/05_limitations.md)
