#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Writes one JSON Lines record per differential path launch when <c>GSHARP_DIFF_RECORD</c> names an output
/// directory (or a test scope is active), so two barrier-model builds can be compared key by key.
/// Inactive runs return immediately with zero allocation.
/// </summary>
internal sealed class DiffRecordWriter
{
    internal const string EnvironmentVariableName = "GSHARP_DIFF_RECORD";
    private const string NativeGetLaunchCountExport = "__gsharp_stack_get_launch_count";
    private const uint CanonicalSingleNaNBits = 0x7FC0_0000u;
    private const ulong CanonicalDoubleNaNBits = 0x7FF8_0000_0000_0000UL;
    private const ushort CanonicalHalfNaNBits = 0x7E00;

    private static readonly DiffRecordWriter? s_envWriter =
        TryCreate(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    private static readonly AsyncLocal<DiffRecordWriter?> s_scopedWriter = new();
    private static readonly ConcurrentDictionary<string, nint> s_nativeCounterExports = new(StringComparer.Ordinal);

    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Case, int Seed, int Shape, DiffPath Path), int> _ordinals = new();
    private readonly ArrayBufferWriter<byte> _jsonBuffer = new();

    private DiffRecordWriter(string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);
        string assemblyName = typeof(DiffRecordWriter).Assembly.GetName().Name ?? "unknown";
        _filePath = Path.Combine(fullDirectory, $"{assemblyName}-{Environment.ProcessId}.jsonl");
    }

    internal static DiffRecordWriter? Active => s_scopedWriter.Value ?? s_envWriter;

    internal string FilePath => _filePath;

    internal static DiffRecordWriter? TryCreate(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        return new DiffRecordWriter(directory);
    }

    internal static IDisposable UseScopeForTests(string? directory)
    {
        var previous = s_scopedWriter.Value;
        s_scopedWriter.Value = TryCreate(directory);
        return new ScopeRestore(previous);
    }

    internal static unsafe long ReadLaunchCount(DiffPath path)
    {
        if (path == DiffPath.Managed)
        {
            return ManagedStackRunner.LaunchCountOnThisThread;
        }

        if (path != DiffPath.Native)
        {
            return 0;
        }

        string dllPath = DiffPaths.NativeLibraryPath(typeof(DiffPaths).Assembly);
        nint export = s_nativeCounterExports.GetOrAdd(dllPath, TryResolveNativeCounterExport);
        if (export == 0)
        {
            return 0;
        }

        ulong count = ((delegate* unmanaged[Cdecl]<ulong>)export)();
        return checked((long)count);
    }

    internal void Write(
        string caseName,
        int seed,
        int shapeIndex,
        DiffPath path,
        LaunchShape shape,
        DiffBuffers? buffers,
        DiffBuffers? ptrBuffers,
        Exception? exception,
        long stackLaunches
    )
    {
        string? outputsHash = ComputeOutputsHash(buffers);
        string? ptrOutputsHash = ComputeOutputsHash(ptrBuffers);
        var unwrapped = exception is null ? null : UnwrapException(exception);
        string? exceptionType = unwrapped is null ? null : (unwrapped.GetType().FullName ?? unwrapped.GetType().Name);
        string? exceptionMessage = unwrapped is null
            ? null
            : NormalizeExceptionMessage(unwrapped.Message, path, shape);

        lock (_lock)
        {
            var key = (caseName, seed, shapeIndex, path);
            _ordinals.TryGetValue(key, out int ordinal);
            _ordinals[key] = ordinal + 1;

            _jsonBuffer.Clear();
            using (var writer = new Utf8JsonWriter(_jsonBuffer))
            {
                writer.WriteStartObject();
                writer.WriteString("case", caseName);
                writer.WriteNumber("seed", seed);
                writer.WriteNumber("shape", shapeIndex);
                writer.WriteString("path", FormatPath(path));
                WriteTriple(writer, "grid", shape.GridX, shape.GridY, shape.GridZ);
                WriteTriple(writer, "block", shape.BlockX, shape.BlockY, shape.BlockZ);
                writer.WriteNumber("ordinal", ordinal);
                WriteStringOrNull(writer, "outputs", outputsHash);
                WriteStringOrNull(writer, "ptrOutputs", ptrOutputsHash);
                WriteException(writer, exceptionType, exceptionMessage);
                writer.WriteNumber("stackLaunches", stackLaunches);
                writer.WriteEndObject();
            }

            var newline = "\n"u8;
            using var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(_jsonBuffer.WrittenSpan);
            stream.Write(newline);
        }
    }

    private static void WriteTriple(Utf8JsonWriter writer, string name, uint x, uint y, uint z)
    {
        writer.WriteStartArray(name);
        writer.WriteNumberValue(x);
        writer.WriteNumberValue(y);
        writer.WriteNumberValue(z);
        writer.WriteEndArray();
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteException(Utf8JsonWriter writer, string? type, string? message)
    {
        if (type is null || message is null)
        {
            writer.WriteNull("exception");
            return;
        }

        writer.WriteStartObject("exception");
        writer.WriteString("type", type);
        writer.WriteString("message", message);
        writer.WriteEndObject();
    }

    internal static string? ComputeOutputsHash(DiffBuffers? buffers)
    {
        if (buffers is null)
        {
            return null;
        }

        List<DiffBuffer>? outputs = null;
        foreach (var buffer in buffers.All)
        {
            if (buffer.IsOutput && !IsAllIgnore(buffer))
            {
                outputs ??= [];
                outputs.Add(buffer);
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (outputs is not null)
        {
            outputs.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            Span<byte> intBuf = stackalloc byte[4];
            Span<byte> tagBuf = stackalloc byte[1];
            foreach (var buffer in outputs)
            {
                AppendOutputBuffer(hash, buffer, intBuf, tagBuf);
            }
        }

        Span<byte> digest = stackalloc byte[32];
        hash.TryGetHashAndReset(digest, out _);
        return $"sha256:{Convert.ToHexStringLower(digest)}";
    }

    // The name of <buffer>, then what its policy says is deterministic: a tag (0 null, 1 raw bytes, 2 formatted cells)
    // and the bytes or cells, a Multiset buffer from its sorted copy
    private static void AppendOutputBuffer(IncrementalHash hash, DiffBuffer buffer, Span<byte> intBuf,
        Span<byte> tagBuf)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(buffer.Name);
        BinaryPrimitives.WriteInt32LittleEndian(intBuf, nameBytes.Length);
        hash.AppendData(intBuf);
        hash.AppendData(nameBytes);

        if (buffer.IsNullArray)
        {
            tagBuf[0] = 0;
            hash.AppendData(tagBuf);
            return;
        }

        var effectiveBuffer = buffer.HasMultisetPolicy ? buffer.CreateSortedCopy() : buffer;
        if (effectiveBuffer.ColumnPolicies is not null && HasMixedColumnHandling(effectiveBuffer.ColumnPolicies))
        {
            AppendMixedColumnBuffer(hash, effectiveBuffer, intBuf, tagBuf);
            return;
        }

        if (!effectiveBuffer.HasCustomComparer &&
            !HasAllCustomColumnComparers(effectiveBuffer.ColumnPolicies) &&
            effectiveBuffer.CanPinAsBytes)
        {
            if (HasAnyCanonicalNaNPolicy(effectiveBuffer) &&
                TryAppendCanonicalNaNFloatingBytes(hash, effectiveBuffer, intBuf, tagBuf))
            {
                return;
            }

            tagBuf[0] = 1;
            hash.AppendData(tagBuf);
            var bytes = effectiveBuffer.AsReadOnlyBytes();
            BinaryPrimitives.WriteInt32LittleEndian(intBuf, bytes.Length);
            hash.AppendData(intBuf);
            hash.AppendData(bytes);
            return;
        }

        // Struct policy (OutputPolicy.ForStruct / Nullable<T>), reference-containing element type
        // (e.g. RefDeepScalar<T>), or empty array: hash cell count and formatted cells rather than
        // raw struct padding or managed pointer bytes.
        tagBuf[0] = 2;
        hash.AppendData(tagBuf);
        BinaryPrimitives.WriteInt32LittleEndian(intBuf, effectiveBuffer.Length);
        hash.AppendData(intBuf);
        for (int i = 0; i < effectiveBuffer.Length; i++)
        {
            AppendFormattedCell(hash, effectiveBuffer, i, intBuf);
        }
    }

    private static bool IsAllIgnore(DiffBuffer buffer)
    {
        if (buffer.ColumnPolicies is null)
        {
            return buffer.Policy.Kind == OutputPolicyKind.Ignore;
        }

        foreach (var colPolicy in buffer.ColumnPolicies)
        {
            if (colPolicy.Kind != OutputPolicyKind.Ignore)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAllCustomColumnComparers(OutputPolicy[]? columnPolicies)
    {
        if (columnPolicies is null || columnPolicies.Length == 0)
        {
            return false;
        }

        foreach (var colPolicy in columnPolicies)
        {
            if (colPolicy.CustomComparer is null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMixedColumnHandling(OutputPolicy[] columnPolicies)
    {
        bool anyCustom = false;
        bool allCustom = true;
        foreach (var colPolicy in columnPolicies)
        {
            if (colPolicy.Kind == OutputPolicyKind.Ignore)
            {
                return true;
            }

            if (colPolicy.CustomComparer is not null)
            {
                anyCustom = true;
            }
            else
            {
                allCustom = false;
            }
        }

        return anyCustom && !allCustom;
    }

    private static bool UsesCanonicalNaN(OutputPolicyKind kind) =>
        kind is OutputPolicyKind.Bitwise or OutputPolicyKind.Multiset;

    private static bool HasAnyCanonicalNaNPolicy(DiffBuffer buffer)
    {
        if (buffer.ColumnPolicies is null)
        {
            return UsesCanonicalNaN(buffer.Policy.Kind);
        }

        foreach (var colPolicy in buffer.ColumnPolicies)
        {
            if (UsesCanonicalNaN(colPolicy.Kind))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAppendCanonicalNaNFloatingBytes(
        IncrementalHash hash,
        DiffBuffer buffer,
        Span<byte> intBuf,
        Span<byte> tagBuf
    )
    {
        int elementSize = GetCanonicalNaNFloatingElementSize(buffer);
        if (elementSize == 0)
        {
            return false;
        }

        tagBuf[0] = 1;
        hash.AppendData(tagBuf);
        BinaryPrimitives.WriteInt32LittleEndian(intBuf, checked(buffer.Length * elementSize));
        hash.AppendData(intBuf);

        Span<byte> scratch = stackalloc byte[512];
        int offset = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            bool canonicalize = UsesCanonicalNaN(buffer.PolicyOf(i).Kind);
            WriteFloatingCell(buffer, i, canonicalize, scratch[offset..]);
            offset += elementSize;
            if (offset == scratch.Length)
            {
                hash.AppendData(scratch);
                offset = 0;
            }
        }

        if (offset > 0)
        {
            hash.AppendData(scratch[..offset]);
        }

        return true;
    }

    private static int GetCanonicalNaNFloatingElementSize(DiffBuffer buffer) => buffer switch
    {
        DiffBuffer<float> => sizeof(float),
        DiffBuffer<double> => sizeof(double),
        DiffBuffer<Half> => sizeof(ushort),
        _ => 0,
    };

    private static void WriteFloatingCell(DiffBuffer buffer, int index, bool canonicalize, Span<byte> destination)
    {
        switch (buffer)
        {
            case DiffBuffer<float> floatBuffer:
            {
                float value = floatBuffer.Data[index];
                uint bits = canonicalize && float.IsNaN(value)
                    ? CanonicalSingleNaNBits
                    : BitConverter.SingleToUInt32Bits(value);
                BinaryPrimitives.WriteUInt32LittleEndian(destination, bits);
                break;
            }
            case DiffBuffer<double> doubleBuffer:
            {
                double value = doubleBuffer.Data[index];
                ulong bits = canonicalize && double.IsNaN(value)
                    ? CanonicalDoubleNaNBits
                    : BitConverter.DoubleToUInt64Bits(value);
                BinaryPrimitives.WriteUInt64LittleEndian(destination, bits);
                break;
            }
            case DiffBuffer<Half> halfBuffer:
            {
                Half value = halfBuffer.Data[index];
                ushort bits = canonicalize && Half.IsNaN(value)
                    ? CanonicalHalfNaNBits
                    : BitConverter.HalfToUInt16Bits(value);
                BinaryPrimitives.WriteUInt16LittleEndian(destination, bits);
                break;
            }
        }
    }

    private static void AppendMixedColumnBuffer(
        IncrementalHash hash,
        DiffBuffer buffer,
        Span<byte> intBuf,
        Span<byte> tagBuf
    )
    {
        tagBuf[0] = 3;
        hash.AppendData(tagBuf);
        BinaryPrimitives.WriteInt32LittleEndian(intBuf, buffer.Length);
        hash.AppendData(intBuf);

        bool canPinAsBytes = buffer.CanPinAsBytes;
        ReadOnlySpan<byte> rawBytes = canPinAsBytes ? buffer.AsReadOnlyBytes() : ReadOnlySpan<byte>.Empty;
        int cellSize = canPinAsBytes ? rawBytes.Length / buffer.Length : 0;
        int floatingElementSize = canPinAsBytes ? GetCanonicalNaNFloatingElementSize(buffer) : 0;
        Span<byte> cellBuf = stackalloc byte[8];

        for (int i = 0; i < buffer.Length; i++)
        {
            var colPolicy = buffer.PolicyOf(i);
            if (colPolicy.Kind == OutputPolicyKind.Ignore)
            {
                continue;
            }

            BinaryPrimitives.WriteInt32LittleEndian(intBuf, i);
            hash.AppendData(intBuf);
            if (!buffer.HasCustomComparer && colPolicy.CustomComparer is null && canPinAsBytes)
            {
                tagBuf[0] = 1;
                hash.AppendData(tagBuf);
                BinaryPrimitives.WriteInt32LittleEndian(intBuf, cellSize);
                hash.AppendData(intBuf);
                if (UsesCanonicalNaN(colPolicy.Kind) && floatingElementSize > 0)
                {
                    WriteFloatingCell(buffer, i, canonicalize: true, cellBuf);
                    hash.AppendData(cellBuf[..floatingElementSize]);
                }
                else
                {
                    hash.AppendData(rawBytes.Slice(i * cellSize, cellSize));
                }
            }
            else
            {
                tagBuf[0] = 2;
                hash.AppendData(tagBuf);
                AppendFormattedCell(hash, buffer, i, intBuf);
            }
        }
    }

    private static void AppendFormattedCell(IncrementalHash hash, DiffBuffer buffer, int index, Span<byte> intBuf)
    {
        byte[] cellBytes = Encoding.UTF8.GetBytes(buffer.FormatCell(index));
        BinaryPrimitives.WriteInt32LittleEndian(intBuf, cellBytes.Length);
        hash.AppendData(intBuf);
        hash.AppendData(cellBytes);
    }

    internal static Exception UnwrapException(Exception exception)
    {
        var current = ExceptionPolicy.Unwrap(exception);
        while (current is AggregateException { InnerExceptions.Count: 1, InnerException: { } inner })
        {
            current = ExceptionPolicy.Unwrap(inner);
        }

        return current;
    }

    internal static string NormalizeExceptionMessage(string message, DiffPath path, LaunchShape shape)
    {
        if (path != DiffPath.Native || shape.TotalBlocks <= 1)
        {
            return message;
        }

        return StripNativeMultiBlockSuffix(message);
    }

    internal static string StripNativeMultiBlockSuffix(string message)
    {
        ReadOnlySpan<char> span = message.AsSpan();
        ReadOnlySpan<char> head = span;
        ReadOnlySpan<char> paramSuffix = ReadOnlySpan<char>.Empty;

        const string ParamMarker = " (Parameter '";
        if (span.EndsWith("')", StringComparison.Ordinal))
        {
            int paramStart = span.LastIndexOf(ParamMarker, StringComparison.Ordinal);
            if (paramStart >= 0)
            {
                ReadOnlySpan<char> candidateName = span.Slice(
                    paramStart + ParamMarker.Length,
                    span.Length - paramStart - ParamMarker.Length - 2);
                if (candidateName.IndexOf('\'') < 0 &&
                    candidateName.IndexOf('\n') < 0 &&
                    candidateName.IndexOf('\r') < 0)
                {
                    head = span[..paramStart];
                    paramSuffix = span[paramStart..];
                }
            }
        }

        const string ThreadMarker = " in thread (";
        int threadStart = head.LastIndexOf(ThreadMarker, StringComparison.Ordinal);
        if (threadStart < 0)
        {
            return message;
        }

        ReadOnlySpan<char> tail = head[(threadStart + ThreadMarker.Length)..];
        if (!TryConsumeCoordTriple(ref tail) ||
            !tail.StartsWith(" of block (", StringComparison.Ordinal))
        {
            return message;
        }

        tail = tail[" of block (".Length..];
        if (!TryConsumeCoordTriple(ref tail) || !tail.IsEmpty)
        {
            return message;
        }

        ReadOnlySpan<char> stripped = head[..threadStart];
        return paramSuffix.IsEmpty ? stripped.ToString() : string.Concat(stripped, paramSuffix);
    }

    private static bool TryConsumeCoordTriple(ref ReadOnlySpan<char> span)
    {
        if (!TryConsumeDigits(ref span) || span.IsEmpty || span[0] != ',')
        {
            return false;
        }

        span = span[1..];
        if (!TryConsumeDigits(ref span) || span.IsEmpty || span[0] != ',')
        {
            return false;
        }

        span = span[1..];
        if (!TryConsumeDigits(ref span) || span.IsEmpty || span[0] != ')')
        {
            return false;
        }

        span = span[1..];
        return true;
    }

    private static bool TryConsumeDigits(ref ReadOnlySpan<char> span)
    {
        int i = 0;
        while (i < span.Length && char.IsAsciiDigit(span[i]))
        {
            i++;
        }

        if (i == 0)
        {
            return false;
        }

        span = span[i..];
        return true;
    }

    private static string FormatPath(DiffPath path) => path switch
    {
        DiffPath.Managed => "Managed",
        DiffPath.Native => "Native",
        DiffPath.Cuda => "Cuda",
        _ => path.ToString(),
    };

    private static nint TryResolveNativeCounterExport(string dllPath)
    {
        if (!File.Exists(dllPath) || !NativeLibrary.TryLoad(dllPath, out nint handle))
        {
            return 0;
        }

        if (NativeLibrary.TryGetExport(handle, NativeGetLaunchCountExport, out nint export))
        {
            return export;
        }

        NativeLibrary.Free(handle);
        return 0;
    }

    private sealed class ScopeRestore(DiffRecordWriter? previous) : IDisposable
    {
        public void Dispose() => s_scopedWriter.Value = previous;
    }
}
