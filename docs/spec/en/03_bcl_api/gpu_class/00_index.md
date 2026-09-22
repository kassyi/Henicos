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
- [WarpShuffleDown](WarpShuffleDown.md): Exchanges variables by shifting data down by a specified delta.
- [WarpShuffleUp](WarpShuffleUp.md): Exchanges variables by shifting data up by a specified delta.
- [WarpShuffleXor](WarpShuffleXor.md): Exchanges variables by applying an XOR operation to lane IDs.
- [GridStrideLoop](GridStrideLoop.md): Executes a standard CUDA grid-stride loop.
- [SingleThread](SingleThread.md): Wraps code in a thread execution guard (Thread 0, Block 0).
- [Constant](Constant.md): Injects string literals as raw C++ constant expressions.
- [HintUnroll](HintUnroll.md): Hints the compiler to unroll the immediately following loop.
- [InlinePtx](InlinePtx.md): Injects raw PTX assembly directly into the generated kernel.
