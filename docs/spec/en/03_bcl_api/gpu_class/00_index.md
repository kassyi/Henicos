---
title: "Gpu Class"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Core static methods for GPU hardware interaction, synchronization, and intrinsics."
---

# Gpu Class

Core static methods for GPU hardware interaction, synchronization, and intrinsics.

## In This Section

- [AtomicAdd](AtomicAdd.md): Performs atomic read-modify-write additions.
- [SyncThreads](SyncThreads.md): Synchronizes all threads in a thread block.
- [SyncGrid](SyncGrid.md): Synchronizes all threads across a cooperative thread grid.
- [WarpShuffleDown](WarpShuffleDown.md): Exchanges variables by shifting data down by a specified delta.
- [WarpShuffleUp](WarpShuffleUp.md): Exchanges variables by shifting data up by a specified delta.
- [WarpShuffleXor](WarpShuffleXor.md): Exchanges variables by applying an XOR operation to lane IDs.
- [GridStrideLoop](GridStrideLoop.md): Runs a body once for each index, spread over every thread of the launch.
- [ForUnroll](ForUnroll.md): Unrolls a loop body at compile-time by duplicating the source code across the specified range.
- [SingleThread](SingleThread.md): Runs the body once per launch, on thread (0,0,0) of block (0,0,0).
- [Constant](Constant.md): Injects string literals as raw C++ constant expressions.
- [HintUnroll](HintUnroll.md): Hints the compiler to unroll the immediately following loop.
- [InlinePtx](InlinePtx.md): Injects raw PTX assembly directly into the generated kernel.
- [NotNull](NotNull.md): Returns a nullable array as a non-nullable one, or throws when it is `null`.

## Intrinsics Without a Page

The following intrinsics have no page of their own yet. Their signatures and XML documentation are generated from the native API definitions (`src/NativeApi/ptx_intrinsics.yml`, `device_heap_intrinsics.yml`, `wmma_intrinsics.yml`) and are shown by IntelliSense.

| Group | Members |
| :--- | :--- |
| `Gpu` atomics | `AtomicSub`, `AtomicOr`, `AtomicAnd`, `AtomicXor`, `AtomicMax`, `AtomicMin`, `AtomicExch`, `AtomicInc`, `AtomicDec`, `AtomicCAS`, `ReductionAdd` |
| `Gpu` integer arithmetic and bits | `AddWithCarry`, `SubWithBorrow`, `MultiplyWide`, `MultiplyHigh`, `MultiplyAddWide`, `MultiplyAddWideWithCarry`, `FunnelShiftLeft`, `FunnelShiftRight`, `BitReverse`, `BitReverse64`, `PopCount`, `CountLeadingZeros`, `CountTrailingZeros`, `BytePermute`, `AbsDiff`, `BitFieldExtract`, `BitFieldInsert`, `Lop3`, `DotProduct4`, `Sad4` |
| `Gpu` fences, clocks and thread identity | `ThreadFence`, `ThreadFenceBlock`, `ThreadFenceSystem`, `Clock`, `Clock64`, `NanoSleep`, `SyncWarp`, `LaneId`, `WarpId`, `SmId`, `MatchAnySync`, `MatchAllSync` |
| `Gpu` memory and caches | `LoadCachedGlobal`, `StoreCachedGlobal`, `Prefetch`, `PrefetchUniform`, `ApplyPriorityL2`, `ConvertToSharedMemory`, `CopyAsync`, `CopyAsyncCommitGroup`, `CopyAsyncWaitGroup`, `CopyAsyncWaitAll`, `MBarrierInit`, `MBarrierArrive`, `MBarrierWait` |
| `Gpu.Unsafe` (device heap) | `Malloc`, `Free` |
| `Warp` | `ActiveMask`, `ShuffleSync`, `BallotSync`, `AllSync`, `AnySync`, `ReduceAddSync`, `ReduceMinSync`, `ReduceMaxSync`, `ReduceAndSync`, `ReduceOrSync`, `ReduceXorSync` (the shuffles with a page are listed above) |
| `Wmma` (Tensor Core warp matrices) | `Fill`, `Load`, `Store`, `MultiplyAccumulate` |

## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)
