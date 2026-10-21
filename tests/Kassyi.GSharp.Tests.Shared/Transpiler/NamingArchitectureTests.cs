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
        // #402: kernel entry points (step 5) and static members other than device functions are still emitted verbatim.
        "MemberAccessExpressionTranspilerRule.cs:node.Name.Identifier.Text",
        "MethodSignatureTranspiler.cs:methodSymbol.ContainingType.Name",
        "MethodSignatureTranspiler.cs:method.Identifier.Text",
        // #411: Gpu.Constant macro names are emitted verbatim until macro injection exists.
        "GpuMacroTranspilerRule.cs:identifier.Identifier.ValueText",
        // Diagnostic and error-comment text, not C++ identifiers.
        "TranspilationSession.cs:methodSymbol.Name",
        "GpuOperatorMacroTranspiler.cs:targetType.Name",
    };

    // C++ / CUDA spellings that legitimately start with "__" and are not compiler-internal names.
    [GeneratedRegex(@"^__(device__|global__|shared__|forceinline__|noinline__|restrict__|declspec|launch_bounds__|syncthreads\w*|syncwarp|shfl\w*|ballot_sync|any_sync|all_sync|activemask|threadfence\w*|popc\w*|clz\w*|ffs\w*|brev\w*|ldg|nanosleep|GSHARP_\w*|NativeContext_\w*)")]
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
        var path = Path.Combine(GetCompilerRoot(), "GSharpInternalNames.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            Assert.False(
                literal.Token.ValueText.StartsWith(GSharpInternalNames.RenamedIdentifierPrefix, StringComparison.Ordinal) ||
                literal.Token.ValueText.StartsWith(GSharpInternalNames.RenamedMemberPrefix, StringComparison.Ordinal),
                $"'{literal.Token.ValueText}' collides with the renamed user identifier families.");
        }

        Assert.DoesNotContain(GSharpInternalNames.RenamedIdentifierPrefix, GSharpPreludeResources.Cpp);
        Assert.DoesNotContain(GSharpInternalNames.RenamedIdentifierPrefix, GSharpPreludeResources.Cuda);
        Assert.DoesNotContain(GSharpInternalNames.RenamedMemberPrefix, GSharpPreludeResources.Cpp);
        Assert.DoesNotContain(GSharpInternalNames.RenamedMemberPrefix, GSharpPreludeResources.Cuda);
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

    private static IEnumerable<(string FileName, SyntaxNode Root)> ParseEmitterSources()
    {
        var compilerRoot = GetCompilerRoot();
        foreach (var directory in s_emitterDirectories)
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(compilerRoot, directory), "*.cs",
                         SearchOption.AllDirectories))
            {
                // The naming layer itself is exempt; ManagedCSharpTranspiler emits C#, not C++.
                var fileName = Path.GetFileName(path);
                if (fileName is "CppNameResolver.cs" or "CppNameFormatter.cs" or "GlobalCppNames.cs" or
                    "ManagedCSharpTranspiler.cs")
                {
                    continue;
                }

                yield return (fileName, CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot());
            }
        }
    }

    private static string GetCompilerRoot([CallerFilePath] string callerFilePath = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath) ?? AppContext.BaseDirectory);
        while (dir != null && dir.GetFiles("Henicos.slnx").Length == 0)
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not locate the repository root (Henicos.slnx).");
        }

        return Path.Combine(dir.FullName, "src", "Compiler", "Kassyi.GSharp.Compiler");
    }
}
