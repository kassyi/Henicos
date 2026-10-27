---
title: "GpuBigFloatTranscendentalOptions Class (G#)"
site: "G# Documentation"
published: "2026-10-27T00:00:00.000Z"
language: "en"
description: "Tuning parameters of the big-float Exp, Log and Pow of one GpuWorkspace."
---

## GpuBigFloatTranscendentalOptions Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Tuning parameters of the big-float transcendental functions (`Exp`, `Log`, `Pow`) of one [GpuWorkspace](../gpu_workspace/GpuWorkspace.md), read through `GpuWorkspace.Transcendental`.

```csharp
public sealed class GpuBigFloatTranscendentalOptions
```

## Fields

| Name | Value | Description |
| --- | --- | --- |
| `MinExpHalvingCount` | 2 | The smallest accepted `ExpHalvingCount`. |
| `MaxExpHalvingCount` | 4096 | The largest accepted `ExpHalvingCount`. |
| `MinSeriesBlockSize` | 2 | The smallest accepted `SeriesBlockSize`. |
| `MaxSeriesBlockSize` | 4096 | The largest accepted `SeriesBlockSize`. |

## Properties

| Name | Description |
| --- | --- |
| [AtanhHornerMaxTerms](#atanhhornermaxterms) | Gets or sets the longest `Log` series evaluated in Horner form. |
| [ExpHalvingCount](#exphalvingcount) | Gets or sets the number of halvings $s$ of `Exp`. |
| [SeriesBlockSize](#seriesblocksize) | Gets or sets the Paterson–Stockmeyer block size $m$ of the `Exp` and `Log` series. |

## AtanhHornerMaxTerms

Gets or sets the largest number of terms of the `Log` series ($\mathrm{atanh}$) that is evaluated in Horner form (one multiplication per term, rounding error below $2.79$ units); a longer series uses Paterson–Stockmeyer (below $m + 1.6$ units for block size $m$). The default is 8, so only arguments close to 1 take Horner form. 0 always uses Paterson–Stockmeyer, `int.MaxValue` always Horner form.

```csharp
public int AtanhHornerMaxTerms { get; set; }
```

#### Exceptions

`ArgumentOutOfRangeException`  
The value is negative.

## ExpHalvingCount

Gets or sets the number $s$ of halvings of `Exp` (and of the `Exp` inside `Pow`): after the reduction $x = k \ln 2 + r$, the series is evaluated at $r / 2^s$ and squared $s$ times. `null` (the default) lets the planner choose.

```csharp
public int? ExpHalvingCount { get; set; }
```

#### Exceptions

`ArgumentOutOfRangeException`  
The value is not `null` and lies outside [`MinExpHalvingCount`, `MaxExpHalvingCount`].

#### Remarks

A larger $s$ needs fewer series terms but amplifies the rounding error by $2^s$, which the planner compensates with more guard bits. The error bound used for that assumes $r / 2^s \le 1/2$, hence the lower limit of 2.

## SeriesBlockSize

Gets or sets the block size $m$ of the Paterson–Stockmeyer evaluation of the `Exp` and `Log` series: the powers $x^1 \ldots x^m$ are computed once and the series is evaluated in Horner form over blocks of $m$ terms, with about $m - 1 + N/m$ full-width multiplications for $N$ terms. `null` (the default) lets the planner choose the $m$ with the fewest multiplications.

```csharp
public int? SeriesBlockSize { get; set; }
```

#### Exceptions

`ArgumentOutOfRangeException`  
The value is not `null` and lies outside [`MinSeriesBlockSize`, `MaxSeriesBlockSize`].

#### Remarks

The rounding error of the `Log` series grows with $m$ (by about one unit per term of a block); the planner adds guard bits for it. Each power takes a buffer of twice the working precision on the device.

## Remarks

A parameter left `null` is chosen per call by the host-side planner from the precision and the argument: the number of full-width multiplications, weighted by the working width. A value overrides that choice. The parameters change only the speed and the working precision; the results do not depend on them, because the guard bits are derived from the error bound of the chosen parameters and the rounding check returns the exact value rounded toward zero in every case. The GPU and the CPU provider read the same options, so they return the same bits.

Set the parameters before the workspace is used from several threads.

## Examples

### Fixing the Halvings of Exp

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Evaluate the Exp series at r / 2^8 with 8 squarings; leave the block size to the planner.
workspace.Transcendental.ExpHalvingCount = 8;
Console.WriteLine($"s = {workspace.Transcendental.ExpHalvingCount}, m = {workspace.Transcendental.SeriesBlockSize?.ToString() ?? "auto"}");
```

## See also

### BCL API > Math Bignum
- [IGpuBigFloatArithmetic Interface](IGpuBigFloatArithmetic.md)
- [GSharpKernelFloatProvider Class](GSharpKernelFloatProvider.md)
- [CSharpCpuBigFloatArithmetic Class](CSharpCpuBigFloatArithmetic.md)
- [GpuWorkspace Class](../gpu_workspace/GpuWorkspace.md)
