---
title: "FermatAlgorithm Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Executes accelerated Fermat number Probable Prime (PRP) tests and NTT-based big integer squaring on NVIDIA GPUs."
---

## FermatAlgorithm Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Executes accelerated Fermat number Probable Prime (PRP) tests and NTT-based big integer squaring kernels on NVIDIA GPUs. Performs iterative squaring and modular reduction modulo $F_E = 2^{2^E} + 1$ using GPU mega-kernels and host orchestration.

```csharp
public class FermatAlgorithm : IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [FermatAlgorithm(int)](#fermatalgorithmint) | Initializes a new instance configured for Fermat exponent `e`. |

## FermatAlgorithm(int)

Initializes a new instance of the `FermatAlgorithm` class configured for Fermat exponent `e`. Allocates the GPU buffers and a dedicated `GpuWorkspace`, and initializes the state to 2.

```csharp
public FermatAlgorithm(int e);
```

#### Parameters

`e` _int_<br>
The Fermat exponent $E$.

#### Exceptions

`InvalidOperationException`<br>
GPU context initialization fails.

## Properties

| Name | Description |
| --- | --- |
| [E](#e) | Gets the Fermat number exponent parameter $E$. |
| [OutNumWords](#outnumwords) | Gets the number of 32-bit words allocated in the output state buffer. |

## E

Gets the Fermat number exponent parameter $E$, where the target test number is $F_E = 2^{2^E} + 1$.

```csharp
public int E { get; }
```

## OutNumWords

Gets the number of 32-bit words allocated in the output state buffer.

```csharp
public int OutNumWords { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Releases all GPU buffers and the workspace. |
| [GetState()](#getstate) | Copies the current state buffer to host memory (blocking). |
| [GetStateAsync(CancellationToken)](#getstateasynccancellationtoken) | Copies the current state buffer to host memory without blocking the calling thread. |
| [LoadState(UInt32[])](#loadstateuint32) | Loads a previously saved state buffer into the GPU. |
| [RunBenchmarkChunk(int)](#runbenchmarkchunkint) | Executes a number of squaring iterations (blocking). |
| [RunBenchmarkChunkAsync(int, CancellationToken)](#runbenchmarkchunkasyncint-cancellationtoken) | Executes a number of squaring iterations, waiting without blocking the calling thread. |
| [RunFermatPrp()](#runfermatprp) | Executes the full Fermat PRP loop (blocking). |
| [RunFermatPrpAsync(CancellationToken)](#runfermatprpasynccancellationtoken) | Executes the full Fermat PRP loop, waiting without blocking the calling thread. |

## Dispose()

Releases all underlying GPU buffers and the workspace allocated by the instance.

```csharp
public void Dispose();
```

## GetState()

Copies and returns the current state buffer (`ValueT`) from GPU device memory to host memory. Blocks the calling thread until queued iterations complete.

```csharp
public uint[] GetState();
```

#### Returns

_uint[]_<br>
An array of 32-bit words containing the current state.

## GetStateAsync(CancellationToken)

Asynchronously copies the current state buffer to host memory, without blocking the calling thread while queued iterations complete.

```csharp
public async ValueTask<uint[]> GetStateAsync(CancellationToken cancellationToken = default);
```

#### Parameters

`cancellationToken` _CancellationToken_<br>
A token that stops waiting; queued device work is not canceled.

#### Returns

_ValueTask&lt;uint[]&gt;_<br>
An array of 32-bit words containing the current state.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## LoadState(UInt32[])

Loads a previously saved state buffer (`ValueT`) into the GPU device execution context.

```csharp
public void LoadState(uint[] valueT);
```

#### Parameters

`valueT` _uint[]_<br>
An array containing the state buffer words to load.

#### Exceptions

`ArgumentException`<br>
The length of `valueT` does not equal `OutNumWords`.

## RunBenchmarkChunk(int)

Executes a specified number of squaring iterations on the GPU for benchmarking purposes and waits for the whole device.

```csharp
public void RunBenchmarkChunk(int chunkSize = 100);
```

#### Parameters

`chunkSize` _int_<br>
The number of squaring iterations to execute.

## RunBenchmarkChunkAsync(int, CancellationToken)

Executes a specified number of squaring iterations like [RunBenchmarkChunk](#runbenchmarkchunkint), but waits for the workspace stream without blocking the calling thread.

```csharp
public ValueTask RunBenchmarkChunkAsync(int chunkSize = 100, CancellationToken cancellationToken = default);
```

#### Parameters

`chunkSize` _int_<br>
The number of squaring iterations to execute.

`cancellationToken` _CancellationToken_<br>
A token that stops waiting. An already canceled token prevents any work; a later cancellation leaves the queued iterations running.

#### Returns

_ValueTask_<br>
A task that completes when the iterations have finished.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## RunFermatPrp()

Executes the full Fermat Probable Prime (PRP) loop on the GPU and returns the final result words and elapsed time. Blocks the calling thread.

```csharp
public (uint[] Result, long ElapsedMs) RunFermatPrp();
```

#### Returns

_(uint[] Result, long ElapsedMs)_<br>
A tuple containing the final state array and the elapsed execution time in milliseconds.

## RunFermatPrpAsync(CancellationToken)

Executes the full Fermat PRP loop like [RunFermatPrp](#runfermatprp), but waits for the GPU without blocking the calling thread.

```csharp
public async ValueTask<(uint[] Result, long ElapsedMs)> RunFermatPrpAsync(CancellationToken cancellationToken = default);
```

#### Parameters

`cancellationToken` _CancellationToken_<br>
A token that stops waiting. An already canceled token prevents any work; a later cancellation leaves the queued iterations running, after which the state is only defined once the workspace stream has drained.

#### Returns

_ValueTask&lt;(uint[] Result, long ElapsedMs)&gt;_<br>
A tuple containing the final state array and the elapsed execution time in milliseconds.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## Remarks

The algorithm is tuned to handle massive numbers over millions of loop iterations entirely within GPU VRAM, bypassing the host CPU to eliminate PCIe transfer bottlenecks. Instead of returning a simple AST node, it dispatches the kernels iteratively and manages the GPU memory lifecycle across multiple passes.

> [!NOTE]
> This type was previously named `FermatRunner` (see [Fermat.md](Fermat.md)); no compatibility wrapper exists.

> [!NOTE]
> In the asynchronous variants only the wait is asynchronous. Graph preparation (one eager warm-up iteration, capture and instantiation) still runs on the calling thread and includes host synchronization. The asynchronous variants wait on the workspace stream rather than the whole device, which is sufficient because every iteration is enqueued on that stream.

## Examples

### Running a Fermat PRP Test Without Blocking a Thread

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU)
using var runner = new FermatAlgorithm(10);

var (result, elapsedMs) = await runner.RunFermatPrpAsync();

Console.WriteLine($"Words={result.Length}, E={runner.E}, Elapsed>=0: {elapsedMs >= 0}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GpuBigUInt Class](../GpuUIntBigInteger.md)
