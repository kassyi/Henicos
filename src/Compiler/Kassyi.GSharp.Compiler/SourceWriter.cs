// StringBuilder is banned in the code that emits source so that it goes through this writer; this is the one writer that owns one.
#pragma warning disable RS0030

namespace Kassyi.GSharp.Compiler;

/// <summary>A zero-allocation writer that builds source text using a pooled thread-static <see cref="StringBuilder"/>.</summary>
#pragma warning disable IDE0051 // Justification: Library methods for Source Generators
public readonly struct SourceWriter : IDisposable, IEquatable<SourceWriter>
{
    [ThreadStatic]
    private static StringBuilder?[]? s_threadStaticBuilders;

    [ThreadStatic]
    private static int s_depth;

    // [WHY] StringBuilder.Clear keeps the capacity, so a pooled builder that once held a whole native module
    // (11 MB for Tests.Cpu) would keep that buffer for the life of the thread. As in Roslyn's
    // PooledStringBuilder.Free and .NET's StringBuilderCache (MaxBuilderSize), a builder that grew past a limit is
    // dropped instead of returned. 32K characters keep a pooled buffer at 64 KB, below the 85,000-byte large object
    // heap threshold, so the pool never holds a large object; a larger text grows a new builder in chunks of at most
    // 8000 characters, which costs no more than the string that ToString makes from it anyway.
    internal const int MaxPooledCapacity = 32 * 1024;

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
        if (Builder is { Capacity: > MaxPooledCapacity } builder
            && s_threadStaticBuilders is { } builders
            && _myDepth < builders.Length
            && ReferenceEquals(builders[_myDepth], builder))
        {
            builders[_myDepth] = null;
        }

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

    /// <summary>
    /// Appends <paramref name="count"/> characters of <paramref name="value"/> from <paramref name="startIndex"/>,
    /// without a substring.
    /// </summary>
    [UnscopedRef]
    public ref readonly SourceWriter Append(string value, int startIndex, int count)
    {
        Builder.Append(value, startIndex, count);
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
        Builder.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ref this;
    }

    [UnscopedRef]
    public ref readonly SourceWriter Append(bool value)
    {
        Builder.Append(value);
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
    public ref readonly SourceWriter AppendVerbatimLiteral(string? text)
    {
        if (text is null)
        {
            return ref this;
        }

        foreach (var c in text)
        {
            if (c == '"')
            {
                Builder.Append("\"\"");
            }
            else
            {
                Builder.Append(c);
            }
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

    /// <summary>Write custom scopes: { anything }</summary>
    public SourceWriterScope Scope(string openingText = "{", string closingText = "}") =>
        new(this, openingText, closingText);

    /// <summary>Write custom inline scopes: { anything }</summary>
    public SourceWriterInlineScope InlineScope(string openingText, string closingText) =>
        new(this, openingText, closingText);

    /// <summary>Write ()</summary>
    public SourceWriterInlineScope Parentheses() => new(this, "(", ")");
    /// <summary>Write []</summary>
    public SourceWriterInlineScope Brackets() => new(this, "[", "]");
    /// <summary>Write {}</summary>
    public SourceWriterInlineScope Braces() => new(this, "{", "}");

    public SourceWriterImmediatelyInvokedLambdaScope ImmediatelyInvokedLambda(string? returnType = null) => new(this, returnType);

    /// <summary>
    /// Write <c>#pragma warning disable &lt;id&gt;[ // &lt;reason&gt;]</c> now and
    /// <c>#pragma warning restore &lt;id&gt;</c> on disposal, or nothing when <paramref name="condition"/> is
    /// <see langword="false"/>.
    /// </summary>
    public SourceWriterWarningSuppression WarningSuppressed(string id, string? reason = null, bool condition = true) =>
        new(this, id, reason, condition);

    /// <summary>Name recorded in the <c>GeneratedCodeAttribute</c> of generated types.</summary>
    public const string GeneratorName = "Kassyi.GSharp.Compiler";

    /// <summary>Version recorded in the <c>GeneratedCodeAttribute</c> of generated types.</summary>
    public const string GeneratorVersion = "1.0.0.0";

    /// <summary>
    /// Writes the boilerplate that opens a generated C# file: the <c>auto-generated</c> marker and, unless
    /// <paramref name="enableNullable"/> is <see langword="false"/>, <c>#nullable enable</c>.
    /// </summary>
    [UnscopedRef]
    public ref readonly SourceWriter AppendGeneratedFileHeader(bool enableNullable = true)
    {
        AppendLine("// <auto-generated/>");
        if (enableNullable)
        {
            AppendLine("#nullable enable");
        }

        return ref this;
    }

    /// <summary>
    /// Writes <c>GeneratedCodeAttribute</c> followed by <c>ExcludeFromCodeCoverage</c> for a generated type or member.
    /// </summary>
    [UnscopedRef]
    public ref readonly SourceWriter AppendGeneratedCodeAttributes()
    {
        AppendLine($"[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{GeneratorName}\", \"{GeneratorVersion}\")]");
        AppendExcludeFromCodeCoverageAttribute();
        return ref this;
    }

    /// <summary>Writes <c>ExcludeFromCodeCoverage</c> alone, for generated types that carry no <c>GeneratedCodeAttribute</c>.</summary>
    [UnscopedRef]
    public ref readonly SourceWriter AppendExcludeFromCodeCoverageAttribute()
    {
        AppendLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        return ref this;
    }


    public static bool operator ==(SourceWriter left, SourceWriter right) => left.Equals(right);

    public static bool operator !=(SourceWriter left, SourceWriter right) => !(left == right);
}
