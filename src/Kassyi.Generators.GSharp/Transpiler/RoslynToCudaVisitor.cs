using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.Generators.GSharp.Diagnostics;

namespace Kassyi.Generators.GSharp.Transpiler;

public partial class RoslynToCudaVisitor(SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null)
    : CSharpSyntaxWalker
{
    public SourceWriter Writer { get; private set; } = new();
    public HashSet<INamedTypeSymbol> UsedStructs { get; } = new(SymbolEqualityComparer.Default);
    public bool HasEmittedDynamicSmemBase
    {
        get; set;
    }
    public SemanticModel SemanticModel => semanticModel;
    public Action<Diagnostic>? ReportDiagnostic => reportDiagnostic;

    public string GenerateCudaSource(MethodDeclarationSyntax method, bool isDevice = false)
    {
        var bodyWriter = new SourceWriter();
        var mainWriter = Writer;
        Writer = bodyWriter;

        UsedStructs.Clear();
        HasEmittedDynamicSmemBase = false;

        WriteMethodSignature(method, isDevice);
        WriteMethodBody(method);

        Writer = mainWriter;

        // Write struct definitions
        var sortedStructs = GetTopologicallySortedStructs();
        foreach (var structSym in sortedStructs)
        {
            WriteStructDefinition(structSym);
        }

        var result = Writer + bodyWriter.ToString();
        Writer.Dispose();
        bodyWriter.Dispose();
        return result;
    }

    public string GenerateCudaSignature(MethodDeclarationSyntax method, bool isDevice = false)
    {
        var mainWriter = Writer;
        var sigWriter = new SourceWriter();
        Writer = sigWriter;

        UsedStructs.Clear();
        WriteMethodSignature(method, isDevice);
        Writer.Append(";");
        Writer.AppendLine();

        Writer = new SourceWriter();
        foreach (var structSym in GetTopologicallySortedStructs())
        {
            var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
            Writer.AppendLine($"struct {structName};");
        }
        Writer.Append(sigWriter.ToString());

        var result = Writer.ToString();
        Writer.Dispose();
        sigWriter.Dispose();
        Writer = mainWriter;
        return result;
    }

    private void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice)
    {
        var methodSymbol = SemanticModel.GetDeclaredSymbol(method);

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol);
        }
        else
        {
            WriteKernelMethodPrefix(method, methodSymbol);
        }

        Writer.AppendSpace();
        Writer.Append(method.Identifier.Text);
        using (Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters);
        }
    }

    private void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol)
    {
        var hasAggressiveInlining = false;
        var hasNoInlining = false;

        if (methodSymbol != null)
        {
            foreach (var attr in methodSymbol.GetAttributes())
            {
                switch (attr.AttributeClass?.Name)
                {
                    case KnownAttributes.GpuInlineAttribute:

                        hasAggressiveInlining = true;
                        break;
                    case KnownAttributes.GpuNoInlineAttribute:

                        hasNoInlining = true;
                        break;
                }
            }
        }

        if (hasAggressiveInlining)
        {
            Writer.AppendKeyword("__forceinline__");
        }
        else if (hasNoInlining)
        {
            Writer.AppendKeyword("__noinline__");
        }
        else
        {
            Writer.AppendKeyword("inline");
        }

        Writer.AppendSpace();
        Writer.AppendKeyword("__device__");
        Writer.AppendSpace();

        var returnType = methodSymbol?.ReturnType;
        if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
        {
            Writer.Append(CudaTypeMapper.MapType(returnType));
        }
        else
        {
            Writer.AppendKeyword("void");
        }
    }

    private void WriteKernelMethodPrefix(MethodDeclarationSyntax method, IMethodSymbol? methodSymbol)
    {
        var returnType = methodSymbol?.ReturnType;
        if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
        {
            ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.KernelReturnMustBeVoid,
                method.ReturnType.GetLocation(), method.Identifier.Text, returnType.ToDisplayString()));
        }

        Writer.AppendKeyword("extern \"C\" __global__ void");
    }

    private void WriteMethodBody(MethodDeclarationSyntax method)
    {
        using (Writer.Scope())
        {
            if (method.Body != null)
            {
                Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                var methodSymbol = SemanticModel.GetDeclaredSymbol(method);
                if (methodSymbol != null && !methodSymbol.ReturnsVoid)
                {
                    Writer.AppendKeyword("return");
                    Writer.AppendSpace();
                }
                Visit(method.ExpressionBody.Expression);
                Writer.AppendSemiColon();
                Writer.AppendLine();
            }
        }
    }

    private void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var validParameters = new List<ParameterSyntax>(parameters.Count);
        foreach (var p in parameters)
        {
            if (!IsJitConstant(p))
            {
                validParameters.Add(p);
            }
        }

        for (var i = 0; i < validParameters.Count; i++)
        {
            var p = validParameters[i];
            var typeSymbol = SemanticModel.GetTypeInfo(p.Type!).Type;

            RegisterUsedStructIfApplicable(typeSymbol);
            var (cppType, isPointer) = CudaTypeMapper.GetParameterCppType(p, typeSymbol);
            CheckLargeStructByValue(p, typeSymbol, isPointer);

            var paramSymbol = SemanticModel.GetDeclaredSymbol(p);
            if (paramSymbol != null && paramSymbol.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name)))
            {
                Writer.AppendKeyword("const");
                Writer.AppendSpace();
            }

            Writer.Append(cppType);
            Writer.AppendSpace();
            Writer.Append(p.Identifier.Text);
            if (i < validParameters.Count - 1)
            {
                Writer.AppendComma();
            }
        }
    }

    private bool IsJitConstant(ParameterSyntax p)
    {
        var paramSymbol = SemanticModel.GetDeclaredSymbol(p);
        if (paramSymbol == null)
        {
            return false;
        }

        foreach (var attr in paramSymbol.GetAttributes())
        {
            if (KnownAttributes.IsJitConstant(attr.AttributeClass?.Name))
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterUsedStructIfApplicable(ITypeSymbol? typeSymbol)
    {
        var structType = typeSymbol is IArrayTypeSymbol arrayType ? arrayType.ElementType : typeSymbol;
        if (structType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct ||
            namedStruct.ContainingNamespace.ToDisplayString() == "System")
        {
            return;
        }

        if (!UsedStructs.Add(namedStruct))
        {
            return;
        }

        foreach (var member in namedStruct.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
        {
            RegisterUsedStructIfApplicable(member.Type);
        }
    }

    private IEnumerable<INamedTypeSymbol> GetTopologicallySortedStructs()
    {
        var result = new List<INamedTypeSymbol>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var s in UsedStructs)
        {
            visit(s);
        }

        return result;

        void visit(INamedTypeSymbol s)
        {
            if (visited.Contains(s))
            {
                return;
            }

            if (!visiting.Add(s))
            {
                return;
            }

            foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                var fieldType = member.Type is IArrayTypeSymbol arrayType ? arrayType.ElementType : member.Type;
                if (fieldType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } namedStruct ||
                    namedStruct.ContainingNamespace.ToDisplayString() == "System")
                {
                    continue;
                }

                if (UsedStructs.Contains(namedStruct))
                {
                    visit(namedStruct);
                }
            }

            visiting.Remove(s);
            visited.Add(s);
            result.Add(s);
        }
    }

    private void CheckLargeStructByValue(ParameterSyntax p, ITypeSymbol? typeSymbol, bool isPointer)
    {
        if (isPointer || typeSymbol is not { TypeKind: TypeKind.Struct } ||
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.SystemNamespace)
        {
            return;
        }

        var size = CudaTypeMapper.GetApproximateSize(typeSymbol);
        if (size >= 32)
        {
            ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.LargeStructByValueWarning,
                p.GetLocation(), p.Identifier.Text, size));
        }
    }

    private void WriteStructDefinition(INamedTypeSymbol structSym)
    {
        var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
        var inlineArrayAttr = structSym.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "InlineArrayAttribute");
        if (inlineArrayAttr != null)
        {
            var length = (int)inlineArrayAttr.ConstructorArguments[0].Value!;
            var firstField = structSym.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);
            if (firstField == null)
            {
                return;
            }

            var cppElementType = CudaTypeMapper.MapType(firstField.Type);
            Writer.AppendLine($"#ifndef __GSHARP_STRUCT_{structName}__");
            Writer.AppendLine($"#define __GSHARP_STRUCT_{structName}__");
            Writer.AppendLine($"struct {structName}");
            using (Writer.Scope())
            {
                Writer.Append($"{cppElementType} {firstField.Name}");
                using (Writer.Brackets())
                {
                    Writer.Append($"{length}");
                }
                Writer.AppendLine(";");

                Writer.Append($"__device__ inline {cppElementType}& operator");
                using (Writer.Brackets()) { }
                using (Writer.Parentheses()) { Writer.Append("int index"); }
                using (Writer.Scope())
                {
                    Writer.Append($"return {firstField.Name}");
                    using (Writer.Brackets()) { Writer.Append("index"); }
                    Writer.AppendLine(";");
                }

                Writer.Append($"__device__ inline const {cppElementType}& operator");
                using (Writer.Brackets()) { }
                using (Writer.Parentheses()) { Writer.Append("int index"); }
                Writer.AppendLine(" const");
                using (Writer.Scope())
                {
                    Writer.Append($"return {firstField.Name}");
                    using (Writer.Brackets()) { Writer.Append("index"); }
                    Writer.AppendLine(";");
                }
            }

            Writer.AppendSemiColon();
            Writer.AppendLine();
            Writer.AppendLine("#endif");
            Writer.AppendLine();

            return;
        }

        Writer.AppendLine($"#ifndef __GSHARP_STRUCT_{structName}__");
        Writer.AppendLine($"#define __GSHARP_STRUCT_{structName}__");
        Writer.AppendLine($"struct {structName}");
        using (Writer.Scope())
        {
            var members = structSym.GetMembers().Where(m => !m.IsStatic && !m.IsImplicitlyDeclared);
            foreach (var member in members)
            {
                switch (member)
                {
                    case IFieldSymbol { IsFixedSizeBuffer: true, Type: IPointerTypeSymbol ptrType } field:
                        {
                            var syntax =
                                field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
                            var sizeExp = syntax?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                            if (sizeExp != null)
                            {
                                Writer.Append($"{CudaTypeMapper.MapType(ptrType.PointedAtType)} {field.Name}");
                                using (Writer.Brackets())
                                {
                                    Visit(sizeExp);
                                }
                                Writer.AppendLine(";");
                            }
                            else
                            {
                                Writer.AppendLine(
                                    $"{CudaTypeMapper.MapType(ptrType.PointedAtType)} {field.Name}; // ERROR: fixed size not found");
                            }

                            break;
                        }
                    case IFieldSymbol field:
                        {
                            var fieldConst = field.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name)) ? "const " : "";
                            Writer.AppendLine($"{fieldConst}{CudaTypeMapper.MapType(field.Type)} {field.Name};");
                            break;
                        }
                    case IPropertySymbol prop:
                        {
                            var propConst = prop.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name)) ? "const " : "";
                            Writer.AppendLine($"{propConst}{CudaTypeMapper.MapType(prop.Type)} {prop.Name};");
                            break;
                        }
                }
            }
        }

        Writer.AppendSemiColon();
        Writer.AppendLine();
        Writer.AppendLine("#endif");
        Writer.AppendLine();
    }

    public void VisitStatementOrBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            using (Writer.Scope())
            {
                foreach (var stmt in block.Statements)
                {
                    Visit(stmt);
                    Writer.AppendLine();
                }
            }
        }
        else
        {
            // Auto-wrap single statements in braces to support C# bracket omission safely in C++
            using (Writer.Scope())
            {
                Visit(statement);
                if (!statement.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.Block))
                {
                    Writer.AppendLine();
                }
            }
        }
    }

    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported, node.GetLocation()));
        }

        base.VisitPredefinedType(node);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, node.GetLocation(),
            node.Kind().ToString()));
        base.DefaultVisit(node);
    }
}
