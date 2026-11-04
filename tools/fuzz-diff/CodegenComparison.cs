using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// What the G# generator wrote for one dialect: the native module and the C# files it added to the compilation.
/// </summary>
/// <param name="NativeModule">The AOT module (<c>.g.cpp</c> for Cpu, <c>.g.cu</c> for Cuda).</param>
/// <param name="ManagedFiles">The generated C# files by file name: launchers, managed kernel bodies, device state machines, preludes.</param>
internal sealed record GeneratorOutputs(string NativeModule, IReadOnlyDictionary<string, string> ManagedFiles);

/// <summary>
/// The first line in which the base ref and the working tree generate different code.
/// </summary>
/// <param name="Kernel">The kernel the difference belongs to, or <see langword="null"/> when it is outside every kernel.</param>
/// <param name="Dialect"><c>Cpu</c> or <c>Cuda</c>.</param>
/// <param name="Output"><see cref="CodegenComparison.NativeOutput"/>, or the name of the generated C# file.</param>
/// <param name="Scope"><c>kernel</c> (one kernel's code), <c>module</c> (the whole native module) or <c>file</c> (a whole C# file).</param>
/// <param name="Line">The 1-based line within the compared kernel code, module or file.</param>
/// <param name="FileLine">The 1-based line in the output file, or 0 when it cannot be located.</param>
/// <param name="BaseText">The line the base ref generates (<c>&lt;EOF&gt;</c> past the end).</param>
/// <param name="HeadText">The line the working tree generates (<c>&lt;EOF&gt;</c> past the end).</param>
internal sealed record CodegenDifference(
    string? Kernel,
    string Dialect,
    string Output,
    string Scope,
    int Line,
    int FileLine,
    string BaseText,
    string HeadText);

/// <summary>
/// Compares the generator output of a base ref with that of the working tree, per kernel where the output can be split
/// by kernel and per module or file otherwise.
/// </summary>
internal static class CodegenComparison
{
    /// <summary>The <see cref="CodegenDifference.Output"/> of the native module.</summary>
    internal const string NativeOutput = "native";

    /// <summary>The <see cref="CodegenDifference.Kernel"/> placeholder text for a difference outside every kernel.</summary>
    internal const string NoKernel = "<module>";

    private const string SymbolHeaderPrefix = "// gsharp-symbols:";
    private const string KernelSourceSuffix = "_Source";
    private const string EndOfFile = "<EOF>";
    private const string MissingFile = "<missing>";

    /// <summary>
    /// Returns the differences between <paramref name="baseOutputs"/> and <paramref name="headOutputs"/> of
    /// <paramref name="dialect"/>: the native module first, then each generated C# file in name order.
    /// </summary>
    internal static List<CodegenDifference> Compare(string dialect, GeneratorOutputs baseOutputs, GeneratorOutputs headOutputs)
    {
        var baseWrapper = SplitWrappers(baseOutputs.ManagedFiles);
        var headWrapper = SplitWrappers(headOutputs.ManagedFiles);

        var differences = new List<CodegenDifference>();
        CompareNative(dialect, baseOutputs.NativeModule, headOutputs.NativeModule, baseWrapper, headWrapper, differences);
        CompareManaged(dialect, baseOutputs.ManagedFiles, headOutputs.ManagedFiles, differences);
        return differences;
    }

    /// <summary>
    /// Normalizes line endings to <c>\n</c>, drops a byte order mark, and drops the <c>// gsharp-symbols: &lt;hash&gt;</c>
    /// first line, whose hash changes with every byte of the module.
    /// </summary>
    internal static string StripSymbolHeader(string text, out bool hadHeader)
    {
        string normalized = Normalize(text);
        hadHeader = normalized.StartsWith(SymbolHeaderPrefix, StringComparison.Ordinal);
        if (!hadHeader)
        {
            return normalized;
        }

        int newline = normalized.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? string.Empty : normalized[(newline + 1)..];
    }

    /// <summary>
    /// Splits a generated launcher file by kernel. The generator writes, per kernel, the constant
    /// <c>{Kernel}_Source</c> with the kernel's native code, followed by that kernel's C# members (full-source
    /// accessor, managed context and state machine, delegate, launcher); the next <c>_Source</c> constant starts the
    /// next kernel. Members before the first constant belong to no kernel and are not returned.
    /// </summary>
    internal static KernelSegments SplitByKernel(string generatedCSharp)
    {
        var nativeSources = new Dictionary<string, string>(StringComparer.Ordinal);
        var managedMembers = new Dictionary<string, string>(StringComparer.Ordinal);
        var managedStartLines = new Dictionary<string, int>(StringComparer.Ordinal);

        var root = CSharpSyntaxTree.ParseText(Normalize(generatedCSharp)).GetRoot();
        foreach (var node in root.DescendantNodes())
        {
            if (node is not TypeDeclarationSyntax type)
            {
                continue;
            }

            string? currentKernel = null;
            foreach (var member in type.Members)
            {
                if (TryGetKernelSource(member, out string? kernel, out string? nativeSource))
                {
                    currentKernel = kernel;
                    nativeSources[kernel] = Normalize(nativeSource);
                    continue;
                }

                if (currentKernel is null)
                {
                    continue;
                }

                string memberText = FuzzSyntaxHelpers.RenderFullNode(member);
                if (managedMembers.TryGetValue(currentKernel, out string? existing))
                {
                    managedMembers[currentKernel] = existing + memberText;
                }
                else
                {
                    managedMembers[currentKernel] = memberText;
                    managedStartLines[currentKernel] = member.SyntaxTree.GetLineSpan(member.FullSpan).StartLinePosition.Line + 1;
                }
            }
        }

        return new KernelSegments(nativeSources, managedMembers, managedStartLines);
    }

    /// <summary>
    /// Returns the first line that differs between <paramref name="baseText"/> and <paramref name="headText"/>, or
    /// <see langword="null"/> when they are equal line by line.
    /// </summary>
    internal static (int Line, string BaseText, string HeadText)? FindFirstLineDifference(string baseText, string headText)
    {
        var baseLines = Normalize(baseText).AsSpan().EnumerateLines();
        var headLines = Normalize(headText).AsSpan().EnumerateLines();
        for (int line = 1; ; line++)
        {
            bool hasBase = baseLines.MoveNext();
            bool hasHead = headLines.MoveNext();
            if (!hasBase && !hasHead)
            {
                return null;
            }

            var b = hasBase ? baseLines.Current : EndOfFile.AsSpan();
            var h = hasHead ? headLines.Current : EndOfFile.AsSpan();
            if (!b.SequenceEqual(h))
            {
                return (line, b.ToString(), h.ToString());
            }
        }
    }

    /// <summary>
    /// A key that is equal for differences that are the same codegen change in different kernels: the dialect, the
    /// output, and the edit between the two lines (what the working tree writes instead of the base, with two characters
    /// on each side), after kernel names (<c>Kernel_07</c>, <c>Kernel_u07</c>) and numbers in names are masked.
    /// </summary>
    /// <example>
    /// <c>v2&lt;&lt;(inC[gid])</c> → <c>v2&lt;&lt; (inC[gid])</c> and <c>v12&lt;&lt;(inA[i])</c> →
    /// <c>v12&lt;&lt; (inA[i])</c> both give the edit <c>&lt;&lt;[| ](i</c>.
    /// </example>
    internal static string GetSignature(CodegenDifference difference)
    {
        const int Context = 2;
        string baseText = MaskDigits(MaskKernelNames(difference.BaseText));
        string headText = MaskDigits(MaskKernelNames(difference.HeadText));

        int prefix = 0;
        while (prefix < baseText.Length && prefix < headText.Length && baseText[prefix] == headText[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < baseText.Length - prefix &&
               suffix < headText.Length - prefix &&
               baseText[baseText.Length - 1 - suffix] == headText[headText.Length - 1 - suffix])
        {
            suffix++;
        }

        string before = baseText[Math.Max(0, prefix - Context)..prefix];
        string after = baseText[(baseText.Length - suffix)..(baseText.Length - suffix + Math.Min(Context, suffix))];
        string removed = baseText[prefix..(baseText.Length - suffix)];
        string added = headText[prefix..(headText.Length - suffix)];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{difference.Dialect}|{difference.Output}|{before}[{removed}|{added}]{after}");
    }

    /// <summary>Replaces each run of ASCII digits with <c>#</c>.</summary>
    internal static string MaskDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                builder.Append(text[i]);
            }
            else if (i == 0 || !char.IsAsciiDigit(text[i - 1]))
            {
                builder.Append('#');
            }
        }

        return builder.ToString();
    }

    /// <summary>Replaces each <c>Kernel_</c> or <c>Kernel_u</c> followed by digits with <c>K</c>.</summary>
    internal static string MaskKernelNames(string text)
    {
        const string Prefix = "Kernel_";
        var builder = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int start = text.IndexOf(Prefix, i, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(text, i, text.Length - i);
                break;
            }

            int end = start + Prefix.Length;
            if (end < text.Length && text[end] == 'u')
            {
                end++;
            }

            int digitsStart = end;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            builder.Append(text, i, start - i);
            if (end > digitsStart)
            {
                builder.Append('K');
                i = end;
            }
            else
            {
                builder.Append(Prefix);
                i = start + Prefix.Length;
            }
        }

        return builder.ToString();
    }

    private static void CompareNative(
        string dialect,
        string baseModule,
        string headModule,
        KernelSegments baseWrapper,
        KernelSegments headWrapper,
        List<CodegenDifference> differences)
    {
        string baseBody = StripSymbolHeader(baseModule, out bool hadHeader);
        string headBody = StripSymbolHeader(headModule, out _);
        if (string.Equals(baseBody, headBody, StringComparison.Ordinal))
        {
            return;
        }

        int headerLines = hadHeader ? 1 : 0;
        int before = differences.Count;
        foreach (var (kernel, baseSource) in baseWrapper.NativeSources)
        {
            if (!headWrapper.NativeSources.TryGetValue(kernel, out string? headSource) ||
                FindFirstLineDifference(baseSource, headSource) is not { } diff)
            {
                continue;
            }

            int offset = LineOffsetOf(baseBody, baseSource);
            differences.Add(new CodegenDifference(
                kernel,
                dialect,
                NativeOutput,
                "kernel",
                diff.Line,
                offset < 0 ? 0 : headerLines + offset + diff.Line,
                diff.BaseText,
                diff.HeadText));
        }

        if (differences.Count == before && FindFirstLineDifference(baseBody, headBody) is { } moduleDiff)
        {
            differences.Add(new CodegenDifference(
                null,
                dialect,
                NativeOutput,
                "module",
                moduleDiff.Line,
                headerLines + moduleDiff.Line,
                moduleDiff.BaseText,
                moduleDiff.HeadText));
        }
    }

    private static void CompareManaged(
        string dialect,
        IReadOnlyDictionary<string, string> baseFiles,
        IReadOnlyDictionary<string, string> headFiles,
        List<CodegenDifference> differences)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        names.UnionWith(baseFiles.Keys);
        names.UnionWith(headFiles.Keys);

        foreach (string name in names)
        {
            if (!baseFiles.TryGetValue(name, out string? b) || !headFiles.TryGetValue(name, out string? h))
            {
                // A file only one side generates.
                differences.Add(new CodegenDifference(
                    null,
                    dialect,
                    name,
                    "file",
                    1,
                    1,
                    b is null ? MissingFile : FirstLine(b),
                    b is null ? FirstLine(headFiles[name]) : MissingFile));
                continue;
            }

            string baseText = Normalize(b);
            string headText = Normalize(h);
            if (string.Equals(baseText, headText, StringComparison.Ordinal))
            {
                continue;
            }

            var baseSegments = SplitByKernel(baseText);
            var headSegments = SplitByKernel(headText);
            int before = differences.Count;
            foreach (var (kernel, baseMembers) in baseSegments.ManagedMembers)
            {
                if (!headSegments.ManagedMembers.TryGetValue(kernel, out string? headMembers) ||
                    FindFirstLineDifference(baseMembers, headMembers) is not { } diff)
                {
                    continue;
                }

                int start = headSegments.ManagedStartLines.TryGetValue(kernel, out int line) ? line : 0;
                differences.Add(new CodegenDifference(
                    kernel,
                    dialect,
                    name,
                    "kernel",
                    diff.Line,
                    start == 0 ? 0 : start + diff.Line - 1,
                    diff.BaseText,
                    diff.HeadText));
            }

            if (differences.Count == before && FindFirstLineDifference(baseText, headText) is { } fileDiff)
            {
                // Only the native code constants differ, which the native comparison reports.
                if (OnlyNativeSourcesDiffer(baseText, headText, baseSegments, headSegments))
                {
                    continue;
                }

                differences.Add(new CodegenDifference(
                    null,
                    dialect,
                    name,
                    "file",
                    fileDiff.Line,
                    fileDiff.Line,
                    fileDiff.BaseText,
                    fileDiff.HeadText));
            }
        }
    }

    // The files differ, the kernel members do not: they are the same once the native code constants are removed.
    private static bool OnlyNativeSourcesDiffer(
        string baseText,
        string headText,
        KernelSegments baseSegments,
        KernelSegments headSegments) =>
        baseSegments.NativeSources.Count > 0 &&
        string.Equals(
            RemoveNativeSources(baseText, baseSegments),
            RemoveNativeSources(headText, headSegments),
            StringComparison.Ordinal);

    private static string RemoveNativeSources(string text, KernelSegments segments)
    {
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var constants = new List<SyntaxNode>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is MemberDeclarationSyntax member && TryGetKernelSource(member, out string? kernel, out _) &&
                segments.NativeSources.ContainsKey(kernel))
            {
                constants.Add(member);
            }
        }

        return root.RemoveNodes(constants, SyntaxRemoveOptions.KeepNoTrivia) is { } remaining ? FuzzSyntaxHelpers.RenderFullNode(remaining) : text;
    }

    private static KernelSegments SplitWrappers(IReadOnlyDictionary<string, string> managedFiles)
    {
        var nativeSources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string text in managedFiles.Values)
        {
            if (!text.Contains(KernelSourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (kernel, source) in SplitByKernel(text).NativeSources)
            {
                nativeSources[kernel] = source;
            }
        }

        return new KernelSegments(nativeSources, new Dictionary<string, string>(), new Dictionary<string, int>());
    }

    // public const string <Kernel>_Source = @"...";
    private static bool TryGetKernelSource(
        MemberDeclarationSyntax member,
        [NotNullWhen(true)] out string? kernel,
        [NotNullWhen(true)] out string? nativeSource)
    {
        if (member is FieldDeclarationSyntax field &&
            field.Modifiers.Any(SyntaxKind.ConstKeyword) &&
            field.Declaration.Variables is [{ Initializer.Value: LiteralExpressionSyntax literal } variable] &&
            literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            variable.Identifier.ValueText is { Length: > 7 } name &&
            name.EndsWith(KernelSourceSuffix, StringComparison.Ordinal))
        {
            kernel = name[..^KernelSourceSuffix.Length];
            nativeSource = literal.Token.ValueText;
            return true;
        }

        kernel = null;
        nativeSource = null;
        return false;
    }

    // 0-based line of the first line of `chunk` within `text`, or -1 when `chunk` does not occur.
    private static int LineOffsetOf(string text, string chunk)
    {
        int index = chunk.Length == 0 ? -1 : text.IndexOf(chunk, StringComparison.Ordinal);
        if (index < 0)
        {
            return -1;
        }

        int lines = 0;
        for (int i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static string FirstLine(string text)
    {
        string normalized = Normalize(text);
        int newline = normalized.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? normalized : normalized[..newline];
    }

    private static string Normalize(string text)
    {
        string withoutBom = text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        return withoutBom.Contains('\r', StringComparison.Ordinal)
            ? withoutBom.Replace("\r\n", "\n", StringComparison.Ordinal)
            : withoutBom;
    }
}

/// <summary>A generated launcher file split by kernel (see <see cref="CodegenComparison.SplitByKernel"/>).</summary>
/// <param name="NativeSources">The native code of each kernel, from its <c>{Kernel}_Source</c> constant.</param>
/// <param name="ManagedMembers">The C# members that follow each kernel's constant, as one text.</param>
/// <param name="ManagedStartLines">The 1-based line of each kernel's first C# member in the file.</param>
internal sealed record KernelSegments(
    IReadOnlyDictionary<string, string> NativeSources,
    IReadOnlyDictionary<string, string> ManagedMembers,
    IReadOnlyDictionary<string, int> ManagedStartLines);
