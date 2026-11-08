using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The <c>System.Array</c> overloads that G# maps in device code, as a table from the overload shape (name, type
/// parameter count, parameter types) to its <see cref="SpanMemberKind"/> (#731).
/// </summary>
internal static class SystemArrayMethods
{
    private static readonly Overload[] s_overloads =
    [
        // Only Array.Sort<T>(T[]) and Array.Sort<TKey, TValue>(TKey[], TValue[]) are supported (#731 Q1=a).
        new("Sort", 1, SpanMemberKind.Sort, Shape.Rank1Array),
        new("Sort", 2, SpanMemberKind.Sort, Shape.Rank1Array, Shape.Rank1Array),
        new("Fill", 1, SpanMemberKind.ArrayFill2, Shape.Rank1Array, Shape.Value),
        new("Fill", 1, SpanMemberKind.ArrayFill4, Shape.Rank1Array, Shape.Value, Shape.Int32, Shape.Int32),
        new("Clear", 0, SpanMemberKind.ArrayClear1, Shape.SystemArray),
        new("Clear", 0, SpanMemberKind.ArrayClear3, Shape.SystemArray, Shape.Int32, Shape.Int32),
        new("Reverse", 0, SpanMemberKind.ArrayReverse1, Shape.AnyArray),
        new("Reverse", 1, SpanMemberKind.ArrayReverse1, Shape.AnyArray),
        new("Reverse", 0, SpanMemberKind.ArrayReverse3, Shape.AnyArray, Shape.Int32, Shape.Int32),
        new("Reverse", 1, SpanMemberKind.ArrayReverse3, Shape.AnyArray, Shape.Int32, Shape.Int32),
        new("Copy", 0, SpanMemberKind.ArrayCopy3, Shape.SystemArray, Shape.SystemArray, Shape.Int32),
        new(
            "Copy",
            0,
            SpanMemberKind.ArrayCopy5,
            Shape.SystemArray,
            Shape.Int32,
            Shape.SystemArray,
            Shape.Int32,
            Shape.Int32
        ),
    ];

    /// <summary>
    /// Classifies a method of <c>System.Array</c> (its original definition); any overload outside the table is
    /// <see cref="SpanMemberKind.Unsupported"/>.
    /// </summary>
    public static SpanMemberKind Classify(IMethodSymbol original) =>
        original.IsStatic &&
        s_overloads
            .AsStackEnumerable()
            .TryFirst(original, static (overload, method) => overload.Matches(method), out var match)
            ? match.Kind
            : SpanMemberKind.Unsupported;

    /// <summary>What one parameter of a mapped overload must be. Every shape is passed by value.</summary>
    private enum Shape
    {
        /// <summary>Any type.</summary>
        Value,

        /// <summary><c>int</c>.</summary>
        Int32,

        /// <summary>A one-dimensional array <c>T[]</c>.</summary>
        Rank1Array,

        /// <summary><c>System.Array</c>.</summary>
        SystemArray,

        /// <summary><c>T[]</c> or <c>System.Array</c>.</summary>
        AnyArray,
    }

    /// <summary>One mapped overload: its name, type parameter count, parameter shapes and classification.</summary>
    private readonly struct Overload(
        string name,
        int typeParameterCount,
        SpanMemberKind kind,
        params Shape[] parameters
    )
    {
        public SpanMemberKind Kind { get; } = kind;

        public bool Matches(IMethodSymbol method)
        {
            if (method.Name != name ||
                method.TypeParameters.Length != typeParameterCount ||
                method.Parameters.Length != parameters.Length)
            {
                return false;
            }

            // Pairs each parameter with the shape at the same position.
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!HasShape(method.Parameters[i], parameters[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasShape(IParameterSymbol parameter, Shape shape) =>
            parameter.RefKind == RefKind.None &&
            shape switch
            {
                Shape.Value => true,
                Shape.Int32 => parameter.Type.SpecialType == SpecialType.System_Int32,
                Shape.Rank1Array => IsRank1Array(parameter.Type),
                Shape.SystemArray => IsSystemArray(parameter.Type),
                Shape.AnyArray => IsRank1Array(parameter.Type) || IsSystemArray(parameter.Type),
                _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
            };

        private static bool IsRank1Array(ITypeSymbol type) =>
            type is IArrayTypeSymbol { Rank: 1 };

        private static bool IsSystemArray(ITypeSymbol type) =>
            type.SpecialType == SpecialType.System_Array;
    }
}
