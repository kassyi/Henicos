using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Transpiler;

public class GEN_05_StructTranspilerEdgeCasesTests
{
    private static INamedTypeSymbol[] GetTypeSymbols(string source, params string[] typeNames)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        
        var references = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var compilation = CSharpCompilation.Create("TestCompilation", new[] { syntaxTree }, references, options);
        
        var diags = compilation.GetDiagnostics();
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            throw new Exception("Compilation errors: " + string.Join(", ", diags.Select(d => d.GetMessage())));
        }

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var decls = syntaxTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax>().ToList();
        
        return typeNames.Select(name => 
            (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(decls.First(s => s.Identifier.Text == name))!
        ).ToArray();
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_EmptyStruct()
    {
        var source = "public struct Empty { }";
        var symbols = GetTypeSymbols(source, "Empty");
        var structSym = symbols[0];

        var usedStructs = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { structSym };
        var context = new TranspileContext(default, usedStructs, new Dictionary<string, OverrideValue>(), null!, null,
            null!);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("Empty", sorted[0].Name);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_DeepNesting()
    {
        var source = @"
            public struct StructA { public StructB b; }
            public struct StructB { public StructC c; }
            public struct StructC { public StructD d; }
            public struct StructD { public int val; }
        ";

        var symbols = GetTypeSymbols(source, "StructA", "StructB", "StructC", "StructD");
        var symA = symbols[0];
        var symB = symbols[1];
        var symC = symbols[2];
        var symD = symbols[3];

        var usedStructs = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { symA, symB, symC, symD };
        
        var fieldB = symA.GetMembers().OfType<IFieldSymbol>().First(f => f.Name == "b");
        Assert.True(SymbolEqualityComparer.Default.Equals(fieldB.Type, symB), "Field type StructB is not equal to symB");

        var context = new TranspileContext(default, usedStructs, new Dictionary<string, OverrideValue>(), null!, null, null!);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Equal(4, sorted.Count);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructD", sorted[0].Name);
        Assert.Equal("StructC", sorted[1].Name);
        Assert.Equal("StructB", sorted[2].Name);
        Assert.Equal("StructA", sorted[3].Name);
    }

    [Fact]
    [Trait("FeatureId", "GEN-05")]
    [Trait("Category", "Unit")]
    public void Test_GEN05_TopologicallySortedStructs_ExcludesSystemTypes()
    {
        var source = @"
            using System;
            public struct MyStruct { public DateTime time; }
        ";
        var symbols = GetTypeSymbols(source, "MyStruct");
        var structSym = symbols[0];

        var usedStructs = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { structSym };
        var context = new TranspileContext(default, usedStructs, new Dictionary<string, OverrideValue>(), null!, null,
            null!);

        var sorted = StructTranspiler.GetTopologicallySortedStructs(ref context).ToList();

        Assert.Single(sorted);
        Assert.Equal("MyStruct", sorted[0].Name);
    }
}
