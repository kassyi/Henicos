
namespace Kassyi.GSharp.Compiler;

/// <summary>A zero-allocation writer that builds source text using a pooled thread-static <see cref="StringBuilder"/>.</summary>
#pragma warning disable IDE0051 // Justification: Library methods for Source Generators
public readonly struct SourceWriter : IDisposable, IEquatable<SourceWriter>
{
    [ThreadStatic]
    private static StringBuilder?[]? s_threadStaticBuilders;

    [ThreadStatic]
    private static int s_depth;

    private readonly int _myDepth;

    private StringBuilder Builder
    {
        get;
    }

    public SourceWriter()
    {
        // [WHY] Reuses a thread-static StringBuilder pool to eliminate heap allocations during source generation.
        s_threadStaticBuilders ??= new StringBuilder[8];
        _myDepth = s_depth++;

        if (_myDepth < s_threadStaticBuilders.Length)
        {
            Builder = s_threadStaticBuilders[_myDepth] ??= new StringBuilder(4096);
        }
        else
        {
            Builder = new StringBuilder(4096);
        }

        Builder.Clear();
    }

    public void Dispose()
    {
        if (_myDepth == s_depth - 1)
        {
            s_depth--;
        }
    }

    [UnscopedRef]
    public ref readonly SourceWriter Append(string? value)
    {
        if (value is not null)
        {
            Builder.Append(value);
        }

        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter Append(char value)
    {
        Builder.Append(value);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter Append(int value)
    {
        Builder.Append(value);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter Append(bool value)
    {
        Builder.Append(value);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendComma()
    {
        Builder.Append(',');
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendSpace()
    {
        Builder.Append(' ');
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendSemiColon()
    {
        Builder.Append(';');
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendSemiColonLine()
    {
        Builder.AppendLine(";");
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendOperator(string op)
    {
        Builder.Append(op);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendKeyword(string keyword)
    {
        Builder.Append(keyword);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendConstantField(IFieldSymbol field)
    {
        if (field.ConstantValue is null)
        {
            Append("nullptr");
            return ref this;
        }

        if (field.Type.SpecialType == SpecialType.System_Boolean)
        {
            Append(((bool)field.ConstantValue) ? "true" : "false");
            return ref this;
        }

        Append(field.ConstantValue.ToString());
        switch (field.Type.SpecialType)
        {
            case SpecialType.System_UInt64:
                Append("ULL");
                break;
            case SpecialType.System_UInt32:
                Append("U");
                break;
            case SpecialType.System_Int64:
                Append("LL");
                break;
        }

        return ref this;
    }

#pragma warning disable CA1822 // Mark members as static
    [UnscopedRef]
    public ref readonly SourceWriter Append(
        [InterpolatedStringHandlerArgument("")]
        ref SourceWriterInterpolatedStringHandler handler) => ref this;

    [UnscopedRef]
    public ref readonly SourceWriter AppendLine()
    {
#pragma warning disable IDE0056
        if (Builder.Length > 0 && Builder[Builder.Length - 1] == '\n')
#pragma warning restore IDE0056
        {
            return ref this;
        }

        Builder.AppendLine();
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendLine(string? value)
    {
        if (value is not null)
        {
            Builder.AppendLine(value);
        }
        else
        {
            AppendLine();
        }

        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendLine(
        [InterpolatedStringHandlerArgument("")]
        ref SourceWriterInterpolatedStringHandler handler)
    {
        AppendLine();
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter Line()
    {
        AppendLine();
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter Line(string? value)
    {
        AppendLine(value);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter Line(
        [InterpolatedStringHandlerArgument("")]
        ref SourceWriterInterpolatedStringHandler handler)
    {
        AppendLine(ref handler);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendIf(bool condition, string? value)
    {
        if (condition && value is not null)
        {
            Builder.Append(value);
        }

        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendIf(bool condition,
        [InterpolatedStringHandlerArgument("", "condition")]
        ref SourceWriterInterpolatedStringHandler handler) =>
        ref this;

    [UnscopedRef]
    public ref readonly SourceWriter AppendLineIf(bool condition, string? value)
    {
        if (condition && value is not null)
        {
            Builder.AppendLine(value);
        }

        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendLineIf(bool condition,
        [InterpolatedStringHandlerArgument("", "condition")]
        ref SourceWriterInterpolatedStringHandler handler)
    {
        if (condition)
        {
            Builder.AppendLine();
        }

        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter LineIf(bool condition, string? value)
    {
        AppendLineIf(condition, value);
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter LineIf(bool condition,
        [InterpolatedStringHandlerArgument("", "condition")]
        ref SourceWriterInterpolatedStringHandler handler)
    {
        AppendLineIf(condition, ref handler);
        return ref this;
    }

    public override string ToString() => Builder.ToString();

    public int Length => Builder.Length;

    internal StringBuilder GetBuilder() => Builder;

#pragma warning restore CA1822

    public bool Equals(SourceWriter other) => this == other;

    public override bool Equals(object? obj) => obj is SourceWriter other && Equals(other);

    public override int GetHashCode() => _myDepth;

    public SourceWriterScope Scope(string openingText = "{", string closingText = "}") =>
        new(this, openingText, closingText);

    public SourceWriterInlineScope InlineScope(string openingText, string closingText) =>
        new(this, openingText, closingText);

    public SourceWriterInlineScope Parentheses() => new(this, "(", ")");
    public SourceWriterInlineScope Brackets() => new(this, "[", "]");
    public SourceWriterInlineScope Braces() => new(this, "{", "}");
    public SourceWriterImmediatelyInvokedLambdaScope ImmediatelyInvokedLambda() => new(this);

    [UnscopedRef]
    public ref readonly SourceWriter AppendParentheses()
    {
        Append("()");
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendBrackets()
    {
        Append("[]");
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendEmptyScope()
    {
        Append("{}");
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter AppendFullyQualifiedCsharpName(ITypeSymbol symbol)
    {
        if (symbol is IArrayTypeSymbol arrayType)
        {
            AppendFullyQualifiedCsharpName(arrayType.ElementType);
            Append("[]");
            return ref this;
        }

        if (symbol is IPointerTypeSymbol pointerType)
        {
            AppendFullyQualifiedCsharpName(pointerType.PointedAtType);
            Append('*');
            return ref this;
        }

        if (symbol is INamedTypeSymbol { TypeArguments.Length: > 0 } namedType)
        {
            Append("global::");
            AppendContainingNamespaceOrType(namedType.ContainingSymbol);
            Append(namedType.Name);
            Append('<');
            for (var i = 0; i < namedType.TypeArguments.Length; i++)
            {
                if (i > 0)
                {
                    Append(", ");
                }

                AppendFullyQualifiedCsharpName(namedType.TypeArguments[i]);
            }

            Append('>');
            return ref this;
        }

        if (symbol.DeclaringSyntaxReferences.Length > 0 &&
            symbol.DeclaringSyntaxReferences[0].GetSyntax() is BaseTypeDeclarationSyntax typeDecl)
        {
            Append("global::");
            AppendParentNamespaceOrTypeFromAst(typeDecl.Parent);
            Append(typeDecl.Identifier.ValueText);
            return ref this;
        }

        Append("global::");
        AppendContainingNamespaceOrType(symbol.ContainingSymbol);
        Append(symbol.Name);
        return ref this;
    }

    private void AppendParentNamespaceOrTypeFromAst(SyntaxNode? parent)
    {
        if (parent is null)
        {
            return;
        }

        if (parent is BaseNamespaceDeclarationSyntax ns)
        {
            AppendParentNamespaceOrTypeFromAst(ns.Parent);
            AppendNameSyntax(ns.Name);
            Append('.');
        }
        else if (parent is BaseTypeDeclarationSyntax parentType)
        {
            AppendParentNamespaceOrTypeFromAst(parentType.Parent);
            Append(parentType.Identifier.ValueText);
            Append('.');
        }
    }

    private void AppendNameSyntax(NameSyntax name)
    {
        if (name is QualifiedNameSyntax q)
        {
            AppendNameSyntax(q.Left);
            Append('.');
            Append(q.Right.Identifier.ValueText);
        }
        else if (name is SimpleNameSyntax s)
        {
            Append(s.Identifier.ValueText);
        }
    }

    private void AppendContainingNamespaceOrType(ISymbol? parent)
    {
        if (parent is null || parent is INamespaceSymbol { IsGlobalNamespace: true })
        {
            return;
        }

        AppendContainingNamespaceOrType(parent.ContainingSymbol);
        Append(parent.Name);
        Append('.');
    }

    public static bool operator ==(SourceWriter left, SourceWriter right) => left.Equals(right);

    public static bool operator !=(SourceWriter left, SourceWriter right) => !(left == right);
}

/// <summary>A zero-allocation scope that appends an opening string on creation and a closing string on disposal.</summary>
public readonly ref struct SourceWriterScope
{
    private readonly SourceWriter _writer;
    private readonly string _closingText;

    public SourceWriterScope(SourceWriter writer, string openingText, string closingText)
    {
        _writer = writer;
        _closingText = closingText;
        if (string.IsNullOrEmpty(openingText))
        {
            return;
        }

        _writer.AppendLine(openingText);
#pragma warning disable CA1865
        if (openingText != "{" && !openingText.EndsWith("{", StringComparison.Ordinal))
#pragma warning restore CA1865
        {
            _writer.AppendLine("{");
        }
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_closingText))
        {
            _writer.AppendLine(_closingText);
        }
    }
}

/// <summary>A zero-allocation inline scope that appends an opening string on creation and a closing string on disposal without adding new lines.</summary>
public readonly ref struct SourceWriterInlineScope
{
    private readonly SourceWriter _writer;
    private readonly string _closingText;

    public SourceWriterInlineScope(SourceWriter writer, string openingText, string closingText)
    {
        _writer = writer;
        _closingText = closingText;
        _writer.Append(openingText);
    }

    public void Dispose() => _writer.Append(_closingText);
}

/// <summary>A zero-allocation scope that wraps content in a C++ immediately invoked lambda ([&]() { ... }()).</summary>
public readonly ref struct SourceWriterImmediatelyInvokedLambdaScope
{
    private readonly SourceWriter _writer;

    public SourceWriterImmediatelyInvokedLambdaScope(SourceWriter writer)
    {
        _writer = writer;
        _writer.AppendLine("([&]() {");
    }

    public void Dispose() => _writer.Append("}())");
}

#pragma warning disable IDE0060 // Remove unused parameter
#pragma warning disable IDE0051 // Remove unused private members
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedParameter.Local
/// <summary>The handler that enables zero-allocation string interpolation using <see cref="SourceWriter"/>.</summary>
[InterpolatedStringHandler]
public readonly ref struct SourceWriterInterpolatedStringHandler
{
    private readonly StringBuilder? _builder;

    public SourceWriterInterpolatedStringHandler(int literalLength, int formattedCount, SourceWriter writer)
    {
        _builder = writer.GetBuilder();
    }

    public SourceWriterInterpolatedStringHandler(int literalLength, int formattedCount, SourceWriter writer,
        bool condition, out bool isHandlerEnabled)
    {
        if (condition)
        {
            _builder = writer.GetBuilder();
            isHandlerEnabled = true;
        }
        else
        {
            _builder = null;
            isHandlerEnabled = false;
        }
    }

    public void AppendLiteral(string s) => _builder?.Append(s);

    public void AppendFormatted(string? s)
    {
        if (s is not null)
        {
            _builder?.Append(s);
        }
    }

    public void AppendFormatted<T>(T t) => _builder?.Append(t);

    public void AppendFormatted(int t) => _builder?.Append(t);
    public void AppendFormatted(uint t) => _builder?.Append(t);
    public void AppendFormatted(long t) => _builder?.Append(t);
    public void AppendFormatted(ulong t) => _builder?.Append(t);
    public void AppendFormatted(short t) => _builder?.Append(t);
    public void AppendFormatted(ushort t) => _builder?.Append(t);
    public void AppendFormatted(byte t) => _builder?.Append(t);
    public void AppendFormatted(sbyte t) => _builder?.Append(t);
    public void AppendFormatted(float t) => _builder?.Append(t);
    public void AppendFormatted(double t) => _builder?.Append(t);
    public void AppendFormatted(decimal t) => _builder?.Append(t);
    public void AppendFormatted(bool t) => _builder?.Append(t);
    public void AppendFormatted(char t) => _builder?.Append(t);

    public void AppendFormatted<T>(T? t, string format) where T : IFormattable =>
        _builder?.Append(t?.ToString(format, null));
}
#pragma warning restore IDE0051
#pragma warning restore IDE0060

