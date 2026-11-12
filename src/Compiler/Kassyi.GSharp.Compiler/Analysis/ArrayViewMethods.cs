using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The instance methods of <c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c> and
/// <c>ImmutableArray&lt;T&gt;</c> that device code calls as the member of the same name of its array view
/// (<c>GSharpArray&lt;T&gt;</c> of the Prelude), which gives them the meaning C# gives them: the same bounds checks,
/// the same exception and the same return value.
/// </summary>
/// <remarks>
/// [WHY] These types are array views in device code, and the standard call emission writes <c>view.Member(args)</c>,
/// which names the view's member. A member of the same name with another meaning
/// (<c>ImmutableArray&lt;T&gt;.Clear()</c> returns an empty array, where the view's <c>Clear()</c> zeroes the
/// elements), or an overload the view does not define (<c>CopyTo(T[], int)</c>, <c>ToArray()</c>), is not listed and
/// is reported as a call the dialect does not implement (GSH0109). The members of <c>Span&lt;T&gt;</c>,
/// <c>ReadOnlySpan&lt;T&gt;</c>, arrays and <c>ImmutableArray&lt;T&gt;.AsSpan()</c> are written by
/// <see cref="SpanWriteClassifier"/>.
/// </remarks>
internal static class ArrayViewMethods
{
    private const ViewType Memories = ViewType.Memory | ViewType.ReadOnlyMemory;

    private static readonly Member[] s_members =
    [
        // Slice(start) and Slice(start, length): ArgumentOutOfRangeException outside the view
        new(Memories | ViewType.ArraySegment, "Slice", Shape.Int32),
        new(Memories | ViewType.ArraySegment | ViewType.ImmutableArray, "Slice", Shape.Int32, Shape.Int32),
        new(ViewType.ImmutableArray, "AsSpan", Shape.Int32, Shape.Int32),
        // CopyTo(destination): ArgumentException when the destination is shorter; TryCopyTo returns false instead
        new(Memories | ViewType.ArraySegment | ViewType.ImmutableArray, "CopyTo", Shape.View),
        new(Memories, "TryCopyTo", Shape.View),
    ];

    /// <summary>The array-like types whose methods the table lists.</summary>
    [Flags]
    private enum ViewType
    {
        None = 0,
        Memory = 1,
        ReadOnlyMemory = 2,
        ArraySegment = 4,
        ImmutableArray = 8,
    }

    /// <summary>What one parameter of a listed method must be. Every shape is passed by value.</summary>
    private enum Shape
    {
        /// <summary><c>int</c>.</summary>
        Int32,

        /// <summary>An array or an array-like type (span, memory, segment, immutable array).</summary>
        View,
    }

    /// <summary>
    /// Whether device code writes <paramref name="method"/> (its original definition or a constructed one) as the
    /// member of the same name of the array view, with the meaning of C#.
    /// </summary>
    public static bool IsViewMember(IMethodSymbol method)
    {
        var original = method.OriginalDefinition;
        if (original is not { IsStatic: false, MethodKind: MethodKind.Ordinary, TypeParameters.Length: 0 } ||
            GetViewType(original.ContainingType) is not (var type and not ViewType.None))
        {
            return false;
        }

        return s_members
            .AsStackEnumerable()
            .Any((original, type), static (member, m) => member.Matches(m.original, m.type));
    }

    private static ViewType GetViewType(INamedTypeSymbol? type) =>
        type is not { IsGenericType: true, TypeArguments.Length: 1 }
            ? ViewType.None
            : type.ContainingNamespace.IsSystemNamespace()
                ? type.Name switch
                {
                    "Memory" => ViewType.Memory,
                    "ReadOnlyMemory" => ViewType.ReadOnlyMemory,
                    "ArraySegment" => ViewType.ArraySegment,
                    _ => ViewType.None,
                }
                : type is { Name: "ImmutableArray" } && type.ContainingNamespace.IsSystemCollectionsImmutable()
                    ? ViewType.ImmutableArray
                    : ViewType.None;

    /// <summary>One listed method: the types that declare it, its name and its parameter shapes.</summary>
    private readonly struct Member(ViewType types, string name, params Shape[] parameters)
    {
        public bool Matches(IMethodSymbol method, ViewType type)
        {
            if ((types & type) == 0 || method.Name != name || method.Parameters.Length != parameters.Length)
            {
                return false;
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = method.Parameters[i];
                if (parameter.RefKind != RefKind.None || !HasShape(parameter.Type, parameters[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasShape(ITypeSymbol type, Shape shape) => shape switch
        {
            Shape.Int32 => type.SpecialType == SpecialType.System_Int32,
            Shape.View => type.TryGetArrayLikeElementType(out _),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }
}
