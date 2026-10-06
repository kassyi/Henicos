---
title: "HeadTailTrackerWriter Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A memory-efficient TextWriter that only keeps track of the first N (Head) and last N (Tail) characters."
---

## HeadTailTrackerWriter Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A memory-efficient `TextWriter` that only keeps track of the first N (Head) and last N (Tail) characters. Intermediate characters are discarded to prevent OutOfMemory exceptions on massive outputs (e.g. 100M+ digits).

```csharp
public class HeadTailTrackerWriter : TextWriter
```

## Constructors

| Name | Description |
| --- | --- |
| [HeadTailTrackerWriter(int, int)](#headtailtrackerwriterint-int) | Initializes a new instance of the `HeadTailTrackerWriter` class. |

## HeadTailTrackerWriter(int, int)

Initializes a new instance of the `HeadTailTrackerWriter` class.

```csharp
public HeadTailTrackerWriter(int headCapacity = 50, int tailCapacity = 50);
```

#### Parameters

`headCapacity` _int_<br>
The number of leading characters to keep.

`tailCapacity` _int_<br>
The number of trailing characters to keep.

## Properties

| Name | Description |
| --- | --- |
| [Encoding](#encoding) | Gets the text encoding in which the output is written. |
| [TotalCharactersWritten](#totalcharacterswritten) | Gets the total number of characters that have been written to this writer. |

## Encoding

Gets the text encoding in which the output is written.

```csharp
public override Encoding Encoding { get; }
```

## TotalCharactersWritten

Gets the total number of characters that have been written to this writer.

```csharp
public long TotalCharactersWritten { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [GetHead()](#gethead) | Returns the head portion of the written text. |
| [GetPreview(string)](#getpreviewstring) | Returns an abbreviated string like "Head...Tail" for preview display. |
| [GetTail()](#gettail) | Returns the tail portion of the written text. |
| [ToString()](#tostring) | Returns the abbreviated preview string representing the written output. |
| [Write(char)](#writechar) | Writes a character to the stream. |
| [Write(char[], int, int)](#writechar-int-int) | Writes a subarray of characters to the stream. |
| [Write(string?)](#writestring) | Writes a string to the stream. |

## GetHead()

Returns the head portion of the written text.

```csharp
public string GetHead();
```

## GetPreview(string)

Returns an abbreviated string like "Head...Tail" for preview display.

```csharp
public string GetPreview(string separator = "...");
```

#### Parameters

`separator` _string_<br>
The string used to separate the head and tail portions when intermediate characters are discarded.

## GetTail()

Returns the tail portion of the written text.

```csharp
public string GetTail();
```

## ToString()

Returns the abbreviated preview string representing the written output.

```csharp
public override string ToString();
```

## Write(char)

Writes a character to the stream.

```csharp
public override void Write(char value);
```

## Write(char[], int, int)

Writes a subarray of characters to the stream.

```csharp
public override void Write(char[] buffer, int index, int count);
```

## Write(string?)

Writes a string to the stream.

```csharp
public override void Write(string? value);
```

## Examples

### Tracking Output Stream Limits

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

var tracker = new HeadTailTrackerWriter(headCapacity: 5, tailCapacity: 3);

// Write exactly 10 characters: "0123456789"
for (int i = 0; i < 10; i++)
{
    tracker.Write(i.ToString());
}

// 0123456789 (length 10)
// Head (5): "01234"
// Tail (3): "789"
// Expected output: 01234...789
Console.WriteLine(tracker.GetPreview());
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
- [GpuBigUInt](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuBigUInt.md)
