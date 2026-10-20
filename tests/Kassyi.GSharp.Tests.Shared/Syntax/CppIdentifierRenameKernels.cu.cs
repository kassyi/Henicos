// ReSharper disable All
using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct CppKeywordInner
{
    public int template;
}

public struct CppKeywordFields
{
    public int register;
    public int @int;
    public int Length;
    public int Count;
    public int _Value;
    public CppKeywordInner inner;
}

[InlineArray(4)]
public struct CppKeywordBuffer4
{
    private int begin;
}

public struct CppKeywordProps
{
    public int union { get; set; }
}

/// <summary>
/// Kernels whose identifiers are legal in C# but are C++ keywords, alternative tokens, CUDA built-ins, names that the
/// generated code emits itself, standard-header macros, or reserved <c>_Upper</c> names.
/// </summary>
public static partial class CppIdentifierRenameKernels
{
    // C++ keywords and alternative operator tokens as parameters and locals.
    [GpuKernel]
    public static void KeywordLocalsKernel(int[] register, int[] template)
    {
        int tid = ThreadIndex.X;
        int union = register[tid];
        int and = union + 1;
        int or = and + 1;
        int not = or + 1;
        int xor = not + 1;
        int compl = xor + 1;
        int typename = compl + 1;
        int mutable = typename + 1;
        template[tid] = union + and + or + not + xor + compl + typename + mutable;
    }

    // Verbatim C# keywords, CUDA built-ins, standard-header macros and reserved names. ThreadIndex.X is emitted as
    // threadIdx.x after a local named threadIdx is in scope.
    [GpuKernel]
    public static void VerbatimAndReservedNamesKernel(int[] @int, int[] @class)
    {
        int threadIdx = @int[ThreadIndex.X];
        int blockIdx = threadIdx + 1;
        int blockDim = blockIdx + 1;
        int gridDim = blockDim + 1;
        int warpSize = gridDim + 1;
        int NULL = warpSize + 1;
        int EOF = NULL + 1;
        int INFINITY = EOF + 1;
        int NAN = INFINITY + 1;
        int RAND_MAX = NAN + 1;
        int EXIT_SUCCESS = RAND_MAX + 1;
        int _Value = EXIT_SUCCESS + 1;
        int @char = _Value + 1;
        @class[ThreadIndex.X] = threadIdx + blockIdx + blockDim + gridDim + warpSize + NULL + EOF + INFINITY + NAN +
                                RAND_MAX + EXIT_SUCCESS + _Value + @char;
    }

    // Locals named after functions that the dialect mapping emits for Math calls in the same scope.
    [GpuKernel]
    public static void MappedFunctionNamedLocalsKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int value = data[tid];
        int min = Math.Min(value, 10);
        int max = Math.Max(value, 10);
        int abs = Math.Abs(min - max);
        int fminf = Math.Min(abs, 5);
        int fmaxf = Math.Max(fminf, 1);
        data[tid] = min + max + abs + fminf + fmaxf;
    }

    [GpuDeviceFunction]
    public static bool TryCombine(int register, int template, out int union)
    {
        int typename = register + template;
        union = typename * 2;
        return union > 0;
    }

    // out var, foreach, local functions, lambdas and tuple swaps declaring reserved names.
    [GpuKernel]
    public static void DeclarationFormsKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int total = 0;
        if (TryCombine(data[tid], 1, out var mutable))
        {
            total += mutable;
        }

        ReadOnlySpan<int> friend = [1, 2, 3];
        foreach (var not in friend)
        {
            total += not;
        }

        int requires(int concept) => concept * 3;
        total += requires(2);

        Func<int, int> constexpr = xor => xor + 4;
        total += constexpr(1);

        int and = 7;
        int or = 8;
        (and, or) = (or, and);
        total += and * 10 + or;

        data[tid] = total;
    }

    // Struct fields, a property, a nested struct field and an InlineArray element with reserved names. Length and Count
    // are also names that the dialect maps for arrays.
    [GpuKernel]
    public static void StructMemberNamesKernel(CppKeywordFields[] items, int[] result)
    {
        int tid = ThreadIndex.X;
        CppKeywordFields item = items[tid];
        CppKeywordBuffer4 buffer = new CppKeywordBuffer4();
        buffer[0] = item.register;
        buffer[1] = item.@int;
        buffer[2] = item.Length;
        buffer[3] = item.Count;

        int sum = 0;
        foreach (var value in buffer)
        {
            sum += value;
        }

        CppKeywordProps props = new CppKeywordProps { union = item._Value };
        item.inner.template = sum + props.union;
        items[tid] = item;
        result[tid] = item.inner.template;
    }

    // Locals and shared memory that live across a barrier are hoisted into the CPU context struct. The parameters share
    // the CPU launcher's scope, which declares bx, tx, ctx and anyActive itself.
    [GpuKernel]
    public static void BarrierHoistedNamesKernel(int[] data, int bx, int tx, int ctx, int anyActive)
    {
        int tid = ThreadIndex.X;
        SharedMemory<int> template = new SharedMemory<int>(32);
        int register = data[tid] + bx + tx + ctx + anyActive;
        template[tid] = register;
        Gpu.SyncThreads();
        int union = template[(tid + 1) % 32];
        data[tid] = register * 1000 + union;
    }

    [GpuKernel]
    public static void GridStrideLambdaParameterKernel(int[] data, int count)
    {
        Gpu.GridStrideLoop(count, union => { data[union] = union * 2; });
    }

    // No local crosses the label: the CPU state machine splits states at user labels without hoisting locals.
    [GpuKernel]
    public static void KeywordLabelKernel(int[] data, int rounds)
    {
        if (rounds > 0)
        {
            goto register;
        }

        data[ThreadIndex.X] = 0;
        return;
    register:
        data[ThreadIndex.X] = data[ThreadIndex.X] * rounds;
    }
}
