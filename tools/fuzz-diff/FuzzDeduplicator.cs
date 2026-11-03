using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Result of computing a canonical AST fingerprint for a reduced differential fuzz kernel.
/// </summary>
public sealed record FuzzFingerprintResult(
    string Fingerprint,
    ulong Hash64,
    string NormalizedAstSkeleton,
    string FailingPathsMask,
    string FailureKindTag);

/// <summary>
/// Computes a normalized AST fingerprint for reduced kernels so duplicate
/// failures with different variable names, method names, custom type names, literal constants, or unreferenced dead declarations
/// collapse to the same bucket:
/// <para>
/// <c>Fingerprint = FNV1a64(NormalizedAstSkeleton + "|" + FailingPathsMask + "|" + FailureKindTag)</c>
/// </para>
/// </summary>
public static class FuzzDeduplicator
{
    /// <summary>The 64-bit FNV-1a offset basis.</summary>
    public const ulong FnvOffsetBasis = 14695981039346656037UL;

    /// <summary>The 64-bit FNV-1a prime multiplier.</summary>
    public const ulong FnvPrime = 1099511628211UL;

    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    private static readonly HashSet<string> s_wellKnownGlobalIdentifiers = new(StringComparer.Ordinal)
    {
        "System",
        "Kassyi",
        "GSharp",
        "Core",
        "Tests",
        "Shared",
        "Differential",
        "GpuKernel",
        "GpuKernelAttribute",
        "GpuDeviceFunction",
        "GpuDeviceFunctionAttribute",
        "BlockIndex",
        "BlockDimension",
        "ThreadIndex",
        "GridDimension",
        "X",
        "Y",
        "Z",
        "Length",
        "Math",
        "MathF",
        "BitOperations",
        "Interlocked",
        "Gpu",
        "Warp",
        "SharedMemory",
        "MinValue",
        "MaxValue",
        "Zero",
        "One",
        "Two",
        "Bit",
        "High",
        "Mask",
        "Alt",
        "Edge",
    };

    /// <summary>
    /// Computes the canonical AST fingerprint and normalized skeleton for <paramref name="kernelSource"/>.
    /// </summary>
    public static FuzzFingerprintResult ComputeFingerprint(
        string kernelSource,
        string failingPaths = "Native,Cuda",
        string failureKind = "CellMismatch")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelSource);

        string canonicalPaths = NormalizeFailingPathsMask(failingPaths);
        string canonicalFailureKind = string.IsNullOrWhiteSpace(failureKind) ? "CellMismatch" : failureKind.Trim();
        string skeleton = BuildNormalizedAstSkeleton(kernelSource);

        string payload = $"{skeleton}|{canonicalPaths}|{canonicalFailureKind}";
        ulong hash64 = ComputeFnv1a64(payload);
        string fingerprint = string.Create(CultureInfo.InvariantCulture, $"fnv1a64:{hash64:x16}");

        return new FuzzFingerprintResult(fingerprint, hash64, skeleton, canonicalPaths, canonicalFailureKind);
    }

    /// <summary>
    /// Computes the 64-bit FNV-1a hash of a UTF-8 string.
    /// </summary>
    public static ulong ComputeFnv1a64(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ulong hash = FnvOffsetBasis;
        int byteCount = Encoding.UTF8.GetByteCount(text);
        byte[] bytes = new byte[byteCount];
        Encoding.UTF8.GetBytes(text, bytes);
        for (int i = 0; i < bytes.Length; i++)
        {
            hash = unchecked((hash ^ bytes[i]) * FnvPrime);
        }

        return hash;
    }

    /// <summary>
    /// Normalizes a failing paths list (e.g. <c>"Cuda, Native"</c>) into a deterministic ordered mask (<c>"Native,Cuda"</c>).
    /// </summary>
    public static string NormalizeFailingPathsMask(string? failingPaths)
    {
        if (string.IsNullOrWhiteSpace(failingPaths))
        {
            return "Unknown";
        }

        var parts = failingPaths
            .Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static p => GetPathOrder(p))
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .Select(static p => NormalizePathName(p));

        string joined = string.Join(",", parts);
        return string.IsNullOrEmpty(joined) ? "Unknown" : joined;
    }

    /// <summary>
    /// Parses <paramref name="kernelSource"/> with Roslyn, strips dead/unreferenced local declarations and redundant parentheses,
    /// alpha-normalizes all identifiers (<c>v0, v1, ...</c> for parameters/locals, <c>M0, M1, ...</c> for methods, <c>T0, T1, ...</c> for custom types),
    /// and normalizes literals to canonical type tokens (<c>LIT_INT</c>, <c>LIT_UINT</c>, <c>LIT_LONG</c>, <c>LIT_ULONG</c>, <c>LIT_BOOL</c>, <c>LIT_CHAR</c>).
    /// </summary>
    public static string BuildNormalizedAstSkeleton(string kernelSource)
    {
        var tree = CSharpSyntaxTree.ParseText(kernelSource, s_parseOptions);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        // Collect custom struct / enum type names in order of first appearance.
        var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (typeDecl is StructDeclarationSyntax or EnumDeclarationSyntax)
            {
                string origName = typeDecl.Identifier.ValueText;
                if (!typeMap.ContainsKey(origName))
                {
                    typeMap[origName] = $"T{typeMap.Count}";
                }
            }
        }

        // Also map known generated fuzz enum names if referenced without declaration in a snippet.
        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            string name = id.Identifier.ValueText;
            if (name.StartsWith("FuzzEnum", StringComparison.Ordinal) && !typeMap.ContainsKey(name))
            {
                typeMap[name] = $"T{typeMap.Count}";
            }
        }

        // Collect kernel and helper methods to include in the skeleton (excluding unused [GpuDeviceFunction] helpers).
        var allMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var kernelMethods = allMethods.Where(static m => FuzzReducer.HasAttribute(m, "GpuKernel")).ToList();
        if (kernelMethods.Count == 0)
        {
            kernelMethods = allMethods;
        }

        var referencedHelperNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var km in kernelMethods)
        {
            foreach (var inv in km.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (inv.Expression is IdentifierNameSyntax id)
                {
                    referencedHelperNames.Add(id.Identifier.ValueText);
                }
            }
        }

        var activeMethods = allMethods
            .Where(m => kernelMethods.Contains(m) || referencedHelperNames.Contains(m.Identifier.ValueText))
            .ToList();

        var methodMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in activeMethods)
        {
            string mName = m.Identifier.ValueText;
            if (!methodMap.ContainsKey(mName))
            {
                methodMap[mName] = $"M{methodMap.Count}";
            }
        }

        var sb = new StringBuilder();
        for (int i = 0; i < activeMethods.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            AppendNormalizedMethod(sb, activeMethods[i], methodMap, typeMap);
        }

        return sb.ToString();
    }

    private static void AppendNormalizedMethod(
        StringBuilder sb,
        MethodDeclarationSyntax method,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        // Step 1: Hoist locals and strip dead/unreferenced local declarations and dead stores inside the method.
        var cleanedMethod = StripDeadLocalsAndStores(method);

        // Step 2: Build alpha-renaming map for parameters and locals in order of first appearance in executable statements.
        var varMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var param in cleanedMethod.ParameterList.Parameters)
        {
            string pName = param.Identifier.ValueText;
            if (!varMap.ContainsKey(pName))
            {
                varMap[pName] = $"v{varMap.Count}";
            }
        }

        var declaredLocals = new HashSet<string>(StringComparer.Ordinal);
        var localTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cleanedMethod.Body is not null)
        {
            foreach (var decl in cleanedMethod.Body.DescendantNodes().OfType<VariableDeclarationSyntax>())
            {
                string normType = NormalizeTypeSyntax(decl.Type, typeMap);
                foreach (var v in decl.Variables)
                {
                    declaredLocals.Add(v.Identifier.ValueText);
                    localTypes[v.Identifier.ValueText] = normType;
                }
            }

            // Assign v0, v1, ... in order of first appearance in non-hoisted executable statements!
            foreach (var stmt in cleanedMethod.Body.Statements)
            {
                if (FuzzReducer.IsHoistedDefaultDeclaration(stmt) &&
                    !FuzzReducer.IsGidDeclaration(stmt) &&
                    !FuzzReducer.IsHashInitDeclaration(stmt))
                {
                    continue;
                }

                foreach (var token in stmt.DescendantTokens())
                {
                    if (token.IsKind(SyntaxKind.IdentifierToken))
                    {
                        string text = token.ValueText;
                        if (declaredLocals.Contains(text) && !varMap.ContainsKey(text))
                        {
                            varMap[text] = $"v{varMap.Count}";
                        }
                    }
                }
            }
        }

        string methodToken = methodMap.TryGetValue(cleanedMethod.Identifier.ValueText, out string? mappedMethod)
            ? mappedMethod
            : "M0";
        string returnType = NormalizeTypeSyntax(cleanedMethod.ReturnType, typeMap);
        sb.Append(returnType).Append(' ').Append(methodToken).Append('(');

        for (int i = 0; i < cleanedMethod.ParameterList.Parameters.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var p = cleanedMethod.ParameterList.Parameters[i];
            string pType = p.Type is not null ? NormalizeTypeSyntax(p.Type, typeMap) : "var";
            string pName = varMap[p.Identifier.ValueText];
            sb.Append(pType).Append(' ').Append(pName);
        }

        sb.Append("){");

        if (cleanedMethod.Body is not null)
        {
            foreach (var stmt in cleanedMethod.Body.Statements)
            {
                // Skip hoisted `T v = default(T);` declarations so declaration order never perturbs the skeleton;
                // local types are already captured at first assignment/use.
                if (FuzzReducer.IsHoistedDefaultDeclaration(stmt) &&
                    !FuzzReducer.IsGidDeclaration(stmt) &&
                    !FuzzReducer.IsHashInitDeclaration(stmt))
                {
                    continue;
                }

                AppendNormalizedStatement(sb, stmt, varMap, methodMap, typeMap, localTypes);
            }
        }
        else if (cleanedMethod.ExpressionBody is not null)
        {
            sb.Append("return ");
            AppendNormalizedExpression(sb, cleanedMethod.ExpressionBody.Expression, varMap, methodMap, typeMap);
            sb.Append(';');
        }

        sb.Append('}');
    }

    private static MethodDeclarationSyntax StripDeadLocalsAndStores(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
        {
            return method;
        }

        // Convert `T v = init;` declarations into hoisted `T v = default(T);` + `v = init;` so dead stores are uniform.
        string wrapped = $"class __Wrapper {{ {method.ToFullString()} }}";
        string hoistedWrapped = FuzzReducer.HoistLocalsToDefault(wrapped);
        var parsedMethod = CSharpSyntaxTree.ParseText(hoistedWrapped, s_parseOptions)
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();

        if (parsedMethod.Body is null)
        {
            return method;
        }

        var statements = parsedMethod.Body.Statements.ToList();

        // Iteratively remove statements that write to a local variable `v` (other than `gid` and `hash`) whose value is never read anywhere afterward.
        for (int pass = 0; pass < 8; pass++)
        {
            bool removedAny = false;
            for (int i = statements.Count - 1; i >= 0; i--)
            {
                var stmt = statements[i];
                if (FuzzReducer.IsHoistedDefaultDeclaration(stmt) ||
                    FuzzReducer.IsGidDeclaration(stmt) ||
                    FuzzReducer.IsGidBoundsGuard(stmt) ||
                    FuzzReducer.IsHashInitDeclaration(stmt) ||
                    FuzzReducer.IsFinalChecksumWrite(stmt))
                {
                    continue;
                }

                if (stmt is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign } &&
                    assign.Left is IdentifierNameSyntax leftId)
                {
                    string targetVar = leftId.Identifier.ValueText;
                    if (targetVar is "gid" or "hash")
                    {
                        continue;
                    }

                    // Check if the RHS has side effects on any OTHER variable.
                    bool rhsMutatesOther = assign.Right.DescendantNodesAndSelf().Any(node =>
                        (node is AssignmentExpressionSyntax) ||
                        (node is PrefixUnaryExpressionSyntax pre && (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression))) ||
                        (node is PostfixUnaryExpressionSyntax post && (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression))));

                    if (rhsMutatesOther)
                    {
                        continue;
                    }

                    bool readLater = false;
                    for (int j = i + 1; j < statements.Count; j++)
                    {
                        if (DoesStatementReadVariable(statements[j], targetVar))
                        {
                            readLater = true;
                            break;
                        }
                    }

                    if (!readLater)
                    {
                        statements.RemoveAt(i);
                        removedAny = true;
                    }
                }
            }

            if (!removedAny)
            {
                break;
            }
        }

        // Remove hoisted `T v = default(T);` declarations for locals that are never referenced in remaining executable statements.
        var referencedVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stmt in statements)
        {
            if (FuzzReducer.IsHoistedDefaultDeclaration(stmt) &&
                !FuzzReducer.IsGidDeclaration(stmt) &&
                !FuzzReducer.IsHashInitDeclaration(stmt))
            {
                continue;
            }

            foreach (var id in stmt.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                referencedVars.Add(id.Identifier.ValueText);
            }
        }

        statements.RemoveAll(stmt =>
            FuzzReducer.IsHoistedDefaultDeclaration(stmt) &&
            !FuzzReducer.IsGidDeclaration(stmt) &&
            !FuzzReducer.IsHashInitDeclaration(stmt) &&
            stmt is LocalDeclarationStatementSyntax ld &&
            ld.Declaration.Variables.Count == 1 &&
            !referencedVars.Contains(ld.Declaration.Variables[0].Identifier.ValueText));

        return parsedMethod.WithBody(parsedMethod.Body.WithStatements(SyntaxFactory.List(statements)));
    }

    private static bool DoesStatementReadVariable(StatementSyntax stmt, string varName)
    {
        foreach (var id in stmt.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Identifier.ValueText != varName)
            {
                continue;
            }

            // If `id` is the direct LHS of a simple assignment `v = expr;`, it is a write, not a read.
            if (id.Parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } simpleAssign &&
                simpleAssign.Left == id)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static void AppendNormalizedStatement(
        StringBuilder sb,
        StatementSyntax stmt,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap,
        IReadOnlyDictionary<string, string> localTypes)
    {
        switch (stmt)
        {
            case BlockSyntax block:
                sb.Append('{');
                foreach (var inner in block.Statements)
                {
                    AppendNormalizedStatement(sb, inner, varMap, methodMap, typeMap, localTypes);
                }

                sb.Append('}');
                break;

            case LocalDeclarationStatementSyntax localDecl:
                string typeText = NormalizeTypeSyntax(localDecl.Declaration.Type, typeMap);
                foreach (var v in localDecl.Declaration.Variables)
                {
                    string vName = varMap.TryGetValue(v.Identifier.ValueText, out string? mapped) ? mapped : v.Identifier.ValueText;
                    sb.Append(typeText).Append(' ').Append(vName);
                    if (v.Initializer is not null)
                    {
                        sb.Append('=');
                        AppendNormalizedExpression(sb, v.Initializer.Value, varMap, methodMap, typeMap);
                    }

                    sb.Append(';');
                }

                break;

            case ExpressionStatementSyntax exprStmt:
                if (exprStmt.Expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "hash" } } hashAssign &&
                    TryNormalizeChecksumFold(hashAssign, varMap, methodMap, typeMap, out string? normalizedFold))
                {
                    sb.Append(normalizedFold).Append(';');
                }
                else
                {
                    AppendNormalizedExpression(sb, exprStmt.Expression, varMap, methodMap, typeMap);
                    sb.Append(';');
                }

                break;

            case IfStatementSyntax ifStmt:
                sb.Append("if(");
                AppendNormalizedExpression(sb, ifStmt.Condition, varMap, methodMap, typeMap);
                sb.Append(')');
                AppendNormalizedStatement(sb, ifStmt.Statement, varMap, methodMap, typeMap, localTypes);
                if (ifStmt.Else is not null)
                {
                    sb.Append("else ");
                    AppendNormalizedStatement(sb, ifStmt.Else.Statement, varMap, methodMap, typeMap, localTypes);
                }

                break;

            case ReturnStatementSyntax retStmt:
                sb.Append("return");
                if (retStmt.Expression is not null)
                {
                    sb.Append(' ');
                    AppendNormalizedExpression(sb, retStmt.Expression, varMap, methodMap, typeMap);
                }

                sb.Append(';');
                break;

            default:
                sb.Append(stmt.WithoutTrivia().ToFullString());
                break;
        }
    }

    private static bool TryNormalizeChecksumFold(
        AssignmentExpressionSyntax hashAssign,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap,
        out string? normalizedFold)
    {
        // Canonicalize `hash = (hash ^ (((ulong)v) + siteIdUL)) * PrimeUL;` so siteId differences don't split identical bugs.
        string hashVar = varMap.TryGetValue("hash", out string? h) ? h : "hash";
        var castNodes = hashAssign.Right.DescendantNodes()
            .OfType<CastExpressionSyntax>()
            .Where(static c => c.Type.ToString() == "ulong")
            .ToList();

        if (castNodes.Count == 1)
        {
            var innerBuilder = new StringBuilder();
            AppendNormalizedExpression(innerBuilder, castNodes[0].Expression, varMap, methodMap, typeMap);
            normalizedFold = $"{hashVar}=FOLD({innerBuilder})";
            return true;
        }

        normalizedFold = null;
        return false;
    }

    private static void AppendNormalizedExpression(
        StringBuilder sb,
        ExpressionSyntax expr,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        expr = UnwrapParentheses(expr);

        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                sb.Append(NormalizeLiteralToken(lit));
                break;

            case DefaultExpressionSyntax defExpr:
                sb.Append("default(").Append(NormalizeTypeSyntax(defExpr.Type, typeMap)).Append(')');
                break;

            case IdentifierNameSyntax id:
                string idText = id.Identifier.ValueText;
                if (varMap.TryGetValue(idText, out string? mappedVar))
                {
                    sb.Append(mappedVar);
                }
                else if (methodMap.TryGetValue(idText, out string? mappedMethod))
                {
                    sb.Append(mappedMethod);
                }
                else if (typeMap.TryGetValue(idText, out string? mappedType))
                {
                    sb.Append(mappedType);
                }
                else
                {
                    sb.Append(idText);
                }

                break;

            case PredefinedTypeSyntax predType:
                sb.Append(predType.Keyword.ValueText);
                break;

            case MemberAccessExpressionSyntax member:
                if (IsConstantBoundaryMemberAccess(member, typeMap, out string? boundaryToken))
                {
                    sb.Append(boundaryToken);
                    break;
                }

                AppendNormalizedExpression(sb, member.Expression, varMap, methodMap, typeMap);
                sb.Append('.');
                string memberName = member.Name.Identifier.ValueText;
                if (s_enumMemberNamesSet.Contains(memberName))
                {
                    sb.Append("LIT_ENUM");
                }
                else
                {
                    sb.Append(memberName);
                }

                break;

            case ElementAccessExpressionSyntax elem:
                AppendNormalizedExpression(sb, elem.Expression, varMap, methodMap, typeMap);
                sb.Append('[');
                for (int i = 0; i < elem.ArgumentList.Arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    AppendNormalizedExpression(sb, elem.ArgumentList.Arguments[i].Expression, varMap, methodMap, typeMap);
                }

                sb.Append(']');
                break;

            case InvocationExpressionSyntax inv:
                AppendNormalizedExpression(sb, inv.Expression, varMap, methodMap, typeMap);
                sb.Append('(');
                for (int i = 0; i < inv.ArgumentList.Arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    var arg = inv.ArgumentList.Arguments[i];
                    if (arg.NameColon is not null)
                    {
                        sb.Append(arg.NameColon.Name.Identifier.ValueText).Append(':');
                    }

                    if (!arg.RefKindKeyword.IsKind(SyntaxKind.None))
                    {
                        sb.Append(arg.RefKindKeyword.ValueText).Append(' ');
                    }

                    AppendNormalizedExpression(sb, arg.Expression, varMap, methodMap, typeMap);
                }

                sb.Append(')');
                break;

            case CastExpressionSyntax cast:
                if (TryCollapseTypedNumericLiteralCast(cast, out string? castLitToken))
                {
                    sb.Append(castLitToken);
                    break;
                }

                sb.Append("((").Append(NormalizeTypeSyntax(cast.Type, typeMap)).Append(')');
                AppendNormalizedExpression(sb, cast.Expression, varMap, methodMap, typeMap);
                sb.Append(')');
                break;

            case PrefixUnaryExpressionSyntax prefix:
                if (prefix.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    UnwrapParentheses(prefix.Operand) is LiteralExpressionSyntax negLit &&
                    negLit.IsKind(SyntaxKind.NumericLiteralExpression))
                {
                    sb.Append(NormalizeNumericLiteralValue(negLit.Token));
                    break;
                }

                sb.Append('(').Append(prefix.OperatorToken.ValueText);
                AppendNormalizedExpression(sb, prefix.Operand, varMap, methodMap, typeMap);
                sb.Append(')');
                break;

            case PostfixUnaryExpressionSyntax postfix:
                sb.Append('(');
                AppendNormalizedExpression(sb, postfix.Operand, varMap, methodMap, typeMap);
                sb.Append(postfix.OperatorToken.ValueText).Append(')');
                break;

            case BinaryExpressionSyntax binary:
                sb.Append('(');
                AppendNormalizedExpression(sb, binary.Left, varMap, methodMap, typeMap);
                sb.Append(binary.OperatorToken.ValueText);
                AppendNormalizedExpression(sb, binary.Right, varMap, methodMap, typeMap);
                sb.Append(')');
                break;

            case AssignmentExpressionSyntax assign:
                AppendNormalizedExpression(sb, assign.Left, varMap, methodMap, typeMap);
                sb.Append(assign.OperatorToken.ValueText);
                AppendNormalizedExpression(sb, assign.Right, varMap, methodMap, typeMap);
                break;

            case ConditionalExpressionSyntax cond:
                sb.Append('(');
                AppendNormalizedExpression(sb, cond.Condition, varMap, methodMap, typeMap);
                sb.Append('?');
                AppendNormalizedExpression(sb, cond.WhenTrue, varMap, methodMap, typeMap);
                sb.Append(':');
                AppendNormalizedExpression(sb, cond.WhenFalse, varMap, methodMap, typeMap);
                sb.Append(')');
                break;

            default:
                sb.Append(expr.WithoutTrivia().ToFullString());
                break;
        }
    }

    private static readonly HashSet<string> s_enumMemberNamesSet = new(StringComparer.Ordinal)
    {
        "Zero", "One", "Two", "Bit", "High", "Mask", "Alt", "Edge",
    };

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        return expr;
    }

    private static bool TryCollapseTypedNumericLiteralCast(CastExpressionSyntax cast, out string? token)
    {
        var inner = UnwrapParentheses(cast.Expression);
        if (inner is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } neg)
        {
            inner = UnwrapParentheses(neg.Operand);
        }

        if (inner is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NumericLiteralExpression })
        {
            string typeName = cast.Type.WithoutTrivia().ToString();
            token = typeName switch
            {
                "sbyte" => "LIT_SBYTE",
                "byte" => "LIT_BYTE",
                "short" => "LIT_SHORT",
                "ushort" => "LIT_USHORT",
                "int" => "LIT_INT",
                "uint" => "LIT_UINT",
                "long" => "LIT_LONG",
                "ulong" => "LIT_ULONG",
                "char" => "LIT_CHAR",
                _ => null,
            };
            return token is not null;
        }

        token = null;
        return false;
    }

    private static bool IsConstantBoundaryMemberAccess(
        MemberAccessExpressionSyntax member,
        IReadOnlyDictionary<string, string> typeMap,
        out string? token)
    {
        string memberName = member.Name.Identifier.ValueText;
        if (memberName is "MinValue" or "MaxValue")
        {
            string targetType = member.Expression.WithoutTrivia().ToString();
            token = targetType switch
            {
                "sbyte" => "LIT_SBYTE",
                "byte" => "LIT_BYTE",
                "short" => "LIT_SHORT",
                "ushort" => "LIT_USHORT",
                "int" => "LIT_INT",
                "uint" => "LIT_UINT",
                "long" => "LIT_LONG",
                "ulong" => "LIT_ULONG",
                "char" => "LIT_CHAR",
                _ => null,
            };
            if (token is not null)
            {
                return true;
            }
        }

        token = null;
        return false;
    }

    private static string NormalizeLiteralToken(LiteralExpressionSyntax lit)
    {
        return lit.Kind() switch
        {
            SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression => "LIT_BOOL",
            SyntaxKind.CharacterLiteralExpression => "LIT_CHAR",
            SyntaxKind.DefaultLiteralExpression => "default",
            SyntaxKind.NullLiteralExpression => "null",
            SyntaxKind.NumericLiteralExpression => NormalizeNumericLiteralValue(lit.Token),
            _ => "LIT",
        };
    }

    private static string NormalizeNumericLiteralValue(SyntaxToken token)
    {
        return token.Value switch
        {
            sbyte => "LIT_SBYTE",
            byte => "LIT_BYTE",
            short => "LIT_SHORT",
            ushort => "LIT_USHORT",
            int => "LIT_INT",
            uint => "LIT_UINT",
            long => "LIT_LONG",
            ulong => "LIT_ULONG",
            char => "LIT_CHAR",
            float => "LIT_FLOAT",
            double => "LIT_DOUBLE",
            _ => "LIT_INT",
        };
    }

    private static string NormalizeTypeSyntax(TypeSyntax typeSyntax, IReadOnlyDictionary<string, string> typeMap)
    {
        return typeSyntax switch
        {
            ArrayTypeSyntax arr => $"{NormalizeTypeSyntax(arr.ElementType, typeMap)}[]",
            NullableTypeSyntax nul => $"{NormalizeTypeSyntax(nul.ElementType, typeMap)}?",
            RefTypeSyntax refType => $"ref {NormalizeTypeSyntax(refType.Type, typeMap)}",
            IdentifierNameSyntax id when typeMap.TryGetValue(id.Identifier.ValueText, out string? mapped) => mapped,
            _ => typeSyntax.WithoutTrivia().ToFullString(),
        };
    }

    private static int GetPathOrder(string path) => path.ToUpperInvariant() switch
    {
        "MANAGED" => 0,
        "NATIVE" or "CPU" => 1,
        "CUDA" or "GPU" => 2,
        _ => 3,
    };

    private static string NormalizePathName(string path) => path.ToUpperInvariant() switch
    {
        "MANAGED" => "Managed",
        "NATIVE" or "CPU" => "Native",
        "CUDA" or "GPU" => "Cuda",
        _ => path,
    };
}
