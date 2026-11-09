#nullable enable
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Layout;

internal static class TypeLayoutTestHelper
{
    internal static readonly ITranspilerDialect s_cudaDialect = new GSharpCudaDialect();
    internal static readonly ITranspilerDialect s_cpuDialect = new StandardCppDialect();

    internal static (Compilation Compilation, SyntaxTree Tree, SemanticModel Model) GetContext() =>
        RoslynTestUtility.GetTestCaseContext(Path.Combine("Layout", "HostLayoutCases.cs"));

    internal static INamedTypeSymbol GetStructSymbol(string structName)
    {
        var (_, tree, model) = GetContext();
        var decl = tree.GetRoot().DescendantNodes()
            .OfType<StructDeclarationSyntax>()
            .First(s => s.Identifier.Text == structName);
        return Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(decl));
    }

    internal static int OffsetOf<TStruct, TField>(ref TStruct s, ref TField field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<TStruct, byte>(ref s), ref Unsafe.As<TField, byte>(ref field));

    internal static void AssertFieldOffset(HostLayout host, string fieldName, int expectedOffset)
    {
        var field = host.Fields.First(f => f.Member.Name == fieldName);
        Assert.Equal(expectedOffset, field.Offset);
    }
}
