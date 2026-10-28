namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Keeps the C++ emitters on the naming layer: user identifiers reach the output only through <c>CppNameResolver</c>,
/// and every name the compiler introduces itself lives under <c>__gsharp_</c>.
/// </summary>
/// <remarks>
/// The checks are syntactic. They catch direct uses (<c>Append(node.Identifier.Text)</c>, <c>$"{field.Name}"</c>) but not
/// a name copied into a local first; the execution tests and the generated-name leak scan cover those.
/// </remarks>
public partial class NamingArchitectureTests
{
    private static readonly string[] s_emitterDirectories = ["Transpiler", "Dialects", "Lowering"];

    // Each entry is "{file}:{expression}" and names the issue that owns the remaining verbatim emission.
    private static readonly HashSet<string> s_allowedRawNames = new(StringComparer.Ordinal)
    {
        // #402: static members other than device functions are still emitted verbatim.
        "MemberAccessExpressionTranspilerRule.cs:node.Name.Identifier.Text",
        // #411: Gpu.Constant macro names are emitted verbatim until macro injection exists.
        "GpuMacroTranspilerRule.cs:identifier.Identifier.ValueText",
        // Diagnostic and error-comment text, not C++ identifiers.
        "TranspilationSession.cs:methodSymbol.Name",
        "GpuOperatorMacroTranspiler.cs:targetType.Name",
        "GpuOperatorMacroTranspiler.cs:targetMethod.ContainingType.Name",
        "GpuOperatorMacroTranspiler.cs:targetMethod.Name",
        "GpuOperatorMacroTranspiler.cs:candidate.Name",
        "GpuOperatorMacroTranspiler.cs:symbol.Name",
        "GpuOperatorMacroTranspiler.cs:resultParameter.Name",
        "GpuOperatorMacroTranspiler.cs:parameter.Name",
        "GpuOperatorMacroTranspiler.cs:method.Name",
        "GpuOperatorMacroTranspiler.cs:innerResult.Name",
        "MethodSignatureTranspiler.cs:methodSymbol.ContainingType.Name",
        "MethodSignatureTranspiler.cs:methodSymbol.Name",
        "HoistedFieldTypeResolver.cs:typeParameter.Name",
        "ArgumentMatchingHelper.cs:callee.Name",
        "ArgumentMatchingHelper.cs:parameter.Name",
        "StateMachineLowerer.cs:labeled.Identifier.ValueText",
        // A kernel whose symbol does not resolve; the C# compilation already fails.
        "MethodSignatureTranspiler.cs:method.Identifier.ValueText",
    };

    // C++ / CUDA spellings that legitimately start with "__" and are not compiler-internal names.
    [GeneratedRegex(@"^__(device__|global__|shared__|forceinline__|noinline__|restrict__|declspec|launch_bounds__|syncthreads\w*|syncwarp|shfl\w*|ballot_sync|any_sync|all_sync|activemask|threadfence\w*|popc\w*|clz\w*|ffs\w*|brev\w*|ldg|nanosleep|GSHARP_\w*)")]
    private static partial Regex AllowedDoubleUnderscoreRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])__[A-Za-z]\w*")]
    private static partial Regex DoubleUnderscoreIdentifierRegex();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Emitters_DoNotAppendRawUserIdentifiers()
    {
        var violations = new List<string>();
        foreach (var (fileName, root) in ParseEmitterSources())
        {
            foreach (var expression in EnumerateEmittedExpressions(root))
            {
                if (expression is not MemberAccessExpressionSyntax memberAccess || !IsRawIdentifierRead(memberAccess))
                {
                    continue;
                }

                var key = $"{fileName}:{memberAccess}";
                if (!s_allowedRawNames.Contains(key))
                {
                    var line = memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{fileName}:{line}: {memberAccess}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Route these user identifiers through CppNameResolver:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Emitters_PrefixInternalNamesWithGSharp()
    {
        var violations = new List<string>();
        foreach (var (fileName, root) in ParseEmitterSources())
        {
            foreach (var token in root.DescendantTokens())
            {
                var text = token.Kind() switch
                {
                    SyntaxKind.StringLiteralToken => token.ValueText,
                    SyntaxKind.InterpolatedStringTextToken => token.ValueText,
                    _ => null
                };

                if (text == null)
                {
                    continue;
                }

                foreach (Match match in DoubleUnderscoreIdentifierRegex().Matches(text))
                {
                    if (match.Value.StartsWith("__gsharp_", StringComparison.Ordinal) ||
                        AllowedDoubleUnderscoreRegex().IsMatch(match.Value))
                    {
                        continue;
                    }

                    var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{fileName}:{line}: {match.Value}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Compiler-internal names must start with __gsharp_ (add them to GSharpInternalNames):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_InternalNames_DoNotUseRenamedFamilyPrefixes()
    {
        foreach (var (path, value, _) in GSharpInternalNames.All)
        {
            Assert.False(
                value.StartsWith(GSharpInternalNames.Renamed.IdentifierPrefix, StringComparison.Ordinal) ||
                value.StartsWith(GSharpInternalNames.Renamed.MemberPrefix, StringComparison.Ordinal),
                $"{path} = '{value}' collides with the renamed user identifier families.");
        }

        Assert.DoesNotContain(GSharpInternalNames.Renamed.IdentifierPrefix, GSharpPreludeResources.Cpp);
        Assert.DoesNotContain(GSharpInternalNames.Renamed.IdentifierPrefix, GSharpPreludeResources.Cuda);
        Assert.DoesNotContain(GSharpInternalNames.Renamed.MemberPrefix, GSharpPreludeResources.Cpp);
        Assert.DoesNotContain(GSharpInternalNames.Renamed.MemberPrefix, GSharpPreludeResources.Cuda);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Compiler_DoesNotRespellInternalNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (namePath, value, _) in GSharpInternalNames.All)
        {
            // LabelPrefix is a prefix by design and would match unrelated text.
            if (value != GSharpInternalNames.Lowering.LabelPrefix)
            {
                names[value] = namePath;
            }
        }

        Assert.NotEmpty(names);

        var violations = new List<string>();
        foreach (var (path, root) in ParseCompilerSources())
        {
            if (Path.GetFileName(path) == "GSharpInternalNames.cs")
            {
                continue;
            }

            foreach (var (text, token) in EnumerateStringTexts(root))
            {
                if (names.TryGetValue(text, out var constant))
                {
                    var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{Path.GetFileName(path)}:{line}: \"{text}\" is GSharpInternalNames.{constant}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Use the GSharpInternalNames constant instead of spelling the name again:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Test_Compiler_StringsDoNotUseRenamedFamilyPrefixes()
    {
        var violations = new List<string>();
        foreach (var (path, root) in ParseCompilerSources())
        {
            // These files encode and decode the renamed families themselves.
            if (Path.GetFileName(path) is "CppNameResolver.cs" or "GSharpInternalNames.cs")
            {
                continue;
            }

            foreach (var (text, token) in EnumerateStringTexts(root))
            {
                if (text.StartsWith(GSharpInternalNames.Renamed.IdentifierPrefix, StringComparison.Ordinal) ||
                    text.StartsWith(GSharpInternalNames.Renamed.MemberPrefix, StringComparison.Ordinal))
                {
                    var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{Path.GetFileName(path)}:{line}: \"{text}\"");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Compiler-internal names must not collide with the renamed user identifier families:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsRawIdentifierRead(MemberAccessExpressionSyntax memberAccess) =>
        memberAccess.Name.Identifier.ValueText switch
        {
            "Text" or "ValueText" => memberAccess.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Identifier"
            },
            "Name" => true,
            _ => false
        };

    // Arguments of Append*/AppendLine* calls and the holes of interpolated strings passed anywhere.
    private static IEnumerable<ExpressionSyntax> EnumerateEmittedExpressions(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method }
                } invocation when method.StartsWith("Append", StringComparison.Ordinal):
                    foreach (var argument in invocation.ArgumentList.Arguments)
                    {
                        yield return argument.Expression;
                    }

                    break;
                case InterpolationSyntax interpolation:
                    yield return interpolation.Expression;
                    break;
            }
        }
    }

    // Text of string literals and of the literal parts of interpolated strings.
    private static IEnumerable<(string Text, SyntaxToken Token)> EnumerateStringTexts(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    yield return (literal.Token.ValueText, literal.Token);
                    break;
                case InterpolatedStringTextSyntax text:
                    yield return (text.TextToken.ValueText, text.TextToken);
                    break;
            }
        }
    }

    private static SyntaxNode ParseCompilerSource(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

    private static IEnumerable<(string Path, SyntaxNode Root)> ParseCompilerSources()
    {
        var compilerRoot = RepositoryPaths.CompilerProject;
        foreach (var path in Directory.EnumerateFiles(compilerRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(compilerRoot, path);
            var firstSegment = relative.Split(Path.DirectorySeparatorChar)[0];
            if (firstSegment is "obj" or "bin")
            {
                continue;
            }

            yield return (path, ParseCompilerSource(path));
        }
    }

    private static IEnumerable<(string FileName, SyntaxNode Root)> ParseEmitterSources()
    {
        var compilerRoot = RepositoryPaths.CompilerProject;
        foreach (var directory in s_emitterDirectories)
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(compilerRoot, directory), "*.cs",
                         SearchOption.AllDirectories))
            {
                // Transpiler/Managed emits C#, not C++.
                var relative = Path.GetRelativePath(compilerRoot, path).Replace('\\', '/');
                if (relative.StartsWith("Transpiler/Managed/", StringComparison.Ordinal))
                {
                    continue;
                }

                // The naming layer itself is exempt.
                var fileName = Path.GetFileName(path);
                if (fileName is "CppNameResolver.cs" or "CppNameFormatter.cs" or "GlobalCppNames.cs")
                {
                    continue;
                }

                yield return (fileName, CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot());
            }
        }
    }
}
