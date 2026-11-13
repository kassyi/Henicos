# Compiler Helper Catalog: Incremental generator models

Part of the [helper catalog](../helpers.md), which also explains how to read the tables (paths, and a method called on
a receiver written with a leading dot) and where a new helper goes.

| Intent | Use | Do not write |
| ----- | ----- | ----- |
| Array field in a pipeline model | `EquatableArray` (`.AsEquatableArray`) | `ImmutableArray<T>` (compares by reference) |
| Stable hash across processes | `HashHelper.ComputeFnv1AHash`, `HashHelper.ComputeFnv1A64`, `HashHelper.Combine` | `string.GetHashCode()` |
| Persisted 64-bit FNV-1a with its own way of feeding input (skip `\r`, mix a line number) | `Fnv1A64` (`Append`, `Value`; `Fnv1A64.Compute` for a whole string) | a hand-written `hash ^= c; hash *= 1099511628211UL` loop |
| The launch limits of the GPUs the project builds for (`GSharpTargetArchitectures`, in `GeneratorConfig`): the compute capabilities the fatbin may run on, the smallest opt-in shared memory per block over them and the compute capability that sets it, and the architectures outside the table (GSH0114); the entries as nvcc reads them, shared with the build tasks (`src/Shared/Cuda`) | `GpuArchitectureLimits.Resolve` (`TargetLimits.MaxSharedMemoryPerBlockOptIn`, `TargetLimits.SharedMemoryLimitArchitecture`), `GpuArchitectureLimits.Table`; `CudaArchitectureList.TryReadEntry`, `CudaArchitectureList.ReadGencodes` | a second table of compute capabilities, the property split by hand, the limit of the lowest compute capability written in place of the strictest one the fatbin may run on |
