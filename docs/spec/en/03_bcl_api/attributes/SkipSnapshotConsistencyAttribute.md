---
title: "SkipSnapshotConsistencyAttribute Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Indicates that a GPU kernel class or method should be skipped during automated snapshot consistency tests."
---

## SkipSnapshotConsistencyAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Indicates that this GPU kernel should be skipped during automated snapshot consistency tests. Use this for work-in-progress kernels or kernels that require multi-file compilation (e.g., `partial` classes) which cannot be compiled in isolation by the snapshot generator.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class SkipSnapshotConsistencyAttribute : Attribute
```

## Constructors

| Name | Description |
| --- | --- |
| [SkipSnapshotConsistencyAttribute(string)](#skipsnapshotconsistencyattributestring) | Initializes a new instance of the `SkipSnapshotConsistencyAttribute` class with an optional exclusion reason. |

## SkipSnapshotConsistencyAttribute(string)

Initializes a new instance of the `SkipSnapshotConsistencyAttribute` class with an optional exclusion reason.

```csharp
public SkipSnapshotConsistencyAttribute(string reason = "");
```

#### Parameters

`reason` _string_<br>
The reason explaining why this kernel is excluded from automated snapshot consistency testing.

## Properties

| Name | Description |
| --- | --- |
| [Reason](#reason) | Gets the reason explaining why snapshot consistency checking is skipped. |

## Reason

Gets the reason explaining why snapshot consistency checking is skipped.

```csharp
public string Reason { get; }
```

## Examples

### Excluding a Multi-File Partial Kernel from Isolated Snapshot Verification

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU)

var attr = typeof(MultiFileKernels).GetCustomAttribute<SkipSnapshotConsistencyAttribute>();
Console.WriteLine($"Skip Reason: {attr?.Reason}");

[SkipSnapshotConsistency("Requires multi-file partial class compilation")]
public static partial class MultiFileKernels
{
}
```

## Remarks

Used by the test suite's snapshot verification harness to bypass isolated single-file compilation checks when a kernel depends on symbols split across multiple `.cu.cs` files.

## See also

### BCL API > Attributes

- [Attributes](00_index.md)
- [GpuKernelAttribute Class (G#)](GpuKernelAttribute.md)
