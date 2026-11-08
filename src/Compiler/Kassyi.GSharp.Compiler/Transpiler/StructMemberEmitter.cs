namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the data members of a device struct definition: the fields in declaration order or, for an explicit layout, in
/// offset order with unions and padding.
/// </summary>
internal static class StructMemberEmitter
{
    /// <summary>
    /// Emits the members of a sequential or auto layout struct in declaration order, padded up to its device size.
    /// </summary>
    public static void WriteSequentialMembers(INamedTypeSymbol structSym, StructLayoutInfo layout,
        ref TranspileContext context)
    {
        foreach (var member in StructAbiMembers.Get(structSym))
        {
            WriteStructMember(ref context, member);
        }

        var end = 0;
        foreach (var field in layout.DeviceFields)
        {
            end = Math.Max(end, field.Offset + field.Layout.Size);
        }

        WriteTailPadding(ref context, 0, end, layout);
    }

    /// <summary>
    /// Emits the members of a <c>[StructLayout(LayoutKind.Explicit)]</c> struct in offset order: the fields that share
    /// an offset go into one anonymous <c>union</c>, the largest first, and a gap before a field or before
    /// <c>StructLayout.Size</c> becomes <c>unsigned char __gsharp_pad_&lt;k&gt;[&lt;n&gt;];</c>.
    /// </summary>
    /// <remarks>
    /// The members of an anonymous union are members of the struct, so member accesses keep their names. Partially
    /// overlapping fields cannot be expressed (GSH0078 reports them); they are emitted in offset order without padding.
    /// </remarks>
    public static void WriteExplicitLayoutMembers(ref TranspileContext context, StructLayoutInfo layout)
    {
        const int MaxStackFields = 64;
        var fields = layout.DeviceFields;
        Span<int> order = fields.Length <= MaxStackFields ? stackalloc int[fields.Length] : new int[fields.Length];
        SortByOffset(fields, order);

        var cursor = 0;
        var padCount = 0;
        for (var start = 0; start < order.Length;)
        {
            var offset = fields[order[start]].Offset;
            var end = start;
            var groupEnd = offset;
            while (end < order.Length && fields[order[end]].Offset == offset)
            {
                groupEnd = Math.Max(groupEnd, offset + fields[order[end]].Layout.Size);
                end++;
            }

            if (offset > cursor)
            {
                WritePadding(ref context, padCount++, offset - cursor);
            }

            WriteOffsetGroup(ref context, fields, order.Slice(start, end - start));
            cursor = Math.Max(cursor, groupEnd);
            start = end;
        }

        WriteTailPadding(ref context, padCount, cursor, layout);
    }

    // [WHY] Insertion sort by offset, the larger field first at one offset: S{} initializes only the first member of an
    // anonymous union, so the largest one zeroes the whole group. Ties keep the declaration order.
    private static void SortByOffset(ImmutableArray<FieldLayout> fields, Span<int> order)
    {
        for (var i = 0; i < order.Length; i++)
        {
            var j = i;
            while (j > 0 && GoesAfter(fields[order[j - 1]], fields[i]))
            {
                order[j] = order[j - 1];
                j--;
            }

            order[j] = i;
        }
    }

    // The fields at one offset: a single member, or union { <members> };
    private static void WriteOffsetGroup(ref TranspileContext context, ImmutableArray<FieldLayout> fields,
        ReadOnlySpan<int> group)
    {
        if (group.Length == 1)
        {
            WriteStructMember(ref context, fields[group[0]].Member);
            return;
        }

        context.Emitter.Writer.Append("union");
        using (context.Emitter.Writer.Scope())
        {
            foreach (var index in group)
            {
                WriteStructMember(ref context, fields[index].Member);
            }
        }

        context.Emitter.Writer.AppendLine(";");
    }

    /// <summary>
    /// Pads a struct whose members end at <paramref name="cursor"/> up to its device size, which
    /// <c>StructLayout.Size</c> can make larger than the members rounded up to the alignment:
    /// <c>unsigned char __gsharp_pad_&lt;padIndex&gt;[&lt;n&gt;];</c>.
    /// </summary>
    /// <remarks>
    /// [WHY] C++ rounds the size up to the alignment only; a larger StructLayout.Size needs explicit bytes (#621).
    /// </remarks>
    internal static void WriteTailPadding(ref TranspileContext context, int padIndex, int cursor,
        StructLayoutInfo layout)
    {
        if (TypeLayoutCalculator.AlignUp(cursor, layout.Device.Alignment) < layout.Device.Size)
        {
            WritePadding(ref context, padIndex, layout.Device.Size - cursor);
        }
    }

    private static bool GoesAfter(FieldLayout earlier, FieldLayout field) =>
        earlier.Offset > field.Offset || (earlier.Offset == field.Offset && earlier.Layout.Size < field.Layout.Size);

    // unsigned char __gsharp_pad_<index>[<bytes>];
    private static void WritePadding(ref TranspileContext context, int index, int bytes) =>
        context.Emitter.Writer.AppendLine($"unsigned char {GSharpInternalNames.Struct.PaddingPrefix}{index}[{bytes}];");

    /// <summary>The C++ name of a struct member.</summary>
    public static string GetMemberName(ISymbol member, ref TranspileContext context) =>
        CppNameResolver.Resolve(member, member.Name, context.Emitter.Dialect).ToString();

    /// <summary>The first member of the native layout of <paramref name="structSym"/>, if it has any.</summary>
    public static bool TryGetFirstMember(INamedTypeSymbol structSym, out StructAbiMember firstMember)
    {
        foreach (var member in StructAbiMembers.Get(structSym))
        {
            firstMember = member;
            return true;
        }

        firstMember = default;
        return false;
    }

    private static SyntaxNode? GetMemberTypeSyntax(ISymbol symbol, Compilation compilation)
    {
        if (symbol.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) &&
            declarator.Parent is VariableDeclarationSyntax varDecl &&
            compilation.ContainsSyntaxTree(varDecl.SyntaxTree))
        {
            return varDecl.Type;
        }

        if (symbol.TryGetDeclaringSyntax<PropertyDeclarationSyntax>(out var propDecl) &&
            compilation.ContainsSyntaxTree(propDecl.SyntaxTree))
        {
            return propDecl.Type;
        }

        return null;
    }

    private static void WriteStructMember(ref TranspileContext context, StructAbiMember member)
    {
        var previousNode = context.Emitter.CurrentNode;
        context.Emitter.CurrentNode =
            GetMemberTypeSyntax(member.Symbol, context.Query.SemanticModel.Compilation);
        try
        {
            WriteStructMemberCore(ref context, member);
        }
        finally
        {
            context.Emitter.CurrentNode = previousNode;
        }
    }

    private static void WriteStructMemberCore(ref TranspileContext context, StructAbiMember member)
    {
        if (member is { Kind: StructAbiMemberKind.FixedBuffer, Symbol: IFieldSymbol field })
        {
            WriteFixedBuffer(ref context, member, field);
            return;
        }

        WriteMemberDeclaration(member.Type, GetMemberName(member.Symbol, ref context), member.Symbol, ref context);
    }

    // <type> <name>[<size>];
    private static void WriteFixedBuffer(ref TranspileContext context, StructAbiMember member, IFieldSymbol field)
    {
        field.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var syntax);

        ExpressionSyntax? sizeExp = null;
        if (syntax?.ArgumentList is { Arguments.Count: > 0 } argList)
        {
            sizeExp = argList.Arguments[0].Expression;
        }

        if (sizeExp != null)
        {
            context.Emitter.Dialect.AppendType(ref context, member.Type);
            context.Emitter.Writer.Append($" {GetMemberName(field, ref context)}");
            using (context.Emitter.Writer.Brackets())
            {
                context.Visitor.Visit(sizeExp);
            }

            context.Emitter.Writer.AppendLine(";");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, member.Type);
            context.Emitter.Writer.AppendLine(
                $"* {GetMemberName(field, ref context)}; // ERROR: fixed size not found");
        }
    }

    private static void WriteMemberDeclaration(ITypeSymbol type, string name, ISymbol memberSymbol,
        ref TranspileContext context)
    {
        var isReadOnly = memberSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
        var isArray = type.TryGetArrayLikeElementType(out var elementType);

        if (isReadOnly && !isArray)
        {
            context.Emitter.Writer.Append("const ");
        }

        // C# 'volatile' forbids caching/reordering of the field access; C++ 'volatile' gives the same guarantee to
        // nvcc/cl.
        if (memberSymbol is IFieldSymbol { IsVolatile: true })
        {
            context.Emitter.Writer.Append("volatile ");
        }

        if (isArray)
        {
            context.Emitter.Writer.Append("GSharpArray<");
            if (isReadOnly)
            {
                context.Emitter.Writer.Append("const ");
            }

            context.Emitter.Dialect.AppendType(ref context, elementType);
            context.Emitter.Writer.Append(">");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, type);
        }

        context.Emitter.Writer.AppendLine($" {name};");
    }
}
