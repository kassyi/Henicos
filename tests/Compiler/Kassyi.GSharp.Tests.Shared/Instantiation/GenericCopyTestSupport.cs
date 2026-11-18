#nullable enable

using Kassyi.GSharp.Compiler.Instantiation;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>The compilation, the copies and the mutated copies that the closed copy tests share.</summary>
internal static class GenericCopyTestSupport
{
    internal static readonly string s_source = RoslynTestUtility.ReadTestCase("Instantiation/GenericCopySource.cs");

    internal static (GenericCopy Copy, GenericCopySet Set) CreateCopy(string method, string typeArgument)
    {
        var compilation = CreateCompilation();
        var type = compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException($"No type '{typeArgument}'.");
        return CreateCopy(compilation, method, type);
    }

    internal static (GenericCopy Copy, GenericCopySet Set) CreateCopy(
        Compilation compilation,
        string method,
        ITypeSymbol type)
    {
        var set = GenericCopies.Create(compilation, [Instance(compilation, method, type)]);
        return (set.Copies.Single(), set);
    }

    // The copy made from the plan without the rewrites of one kind
    internal static GenericCopy CreateWithout<TRewrite>(string method, string typeArgument) where TRewrite : CopyRewrite
    {
        var compilation = CreateCompilation();
        var type = compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException($"No type '{typeArgument}'.");
        var plan = GenericCopies.Plan(compilation, Instance(compilation, method, type));
        var removed = plan.Rewrites.Values.OfType<TRewrite>().Select(r => r.NodeIndex).ToArray();
        Assert.NotEmpty(removed);

        var withoutThem = plan with { Rewrites = plan.Rewrites.RemoveRange(removed) };
        return GenericCopies.Create(compilation, [withoutThem]).Copies.Single();
    }

    internal static GenericInstance Instance(Compilation compilation, string method, ITypeSymbol type)
    {
        var kernels = compilation.GetTypeByMetadataName("App.Kernels") ??
                      throw new InvalidOperationException("No Kernels.");
        var definition = kernels.GetMembers(method).OfType<IMethodSymbol>().Single(m => m.IsGenericMethod);
        return GenericInstance.Of(definition.Construct(type));
    }

    internal static int LineOf(string marker)
    {
        var text = SourceText.From(s_source);
        return text.Lines.First(line => text.ToString(line.Span).Contains(marker, StringComparison.Ordinal)).LineNumber;
    }

    internal static string Describe(GenericCopy copy) =>
        string.Join("; ", copy.Findings.Select(f => $"{f.Location.GetLineSpan().StartLinePosition}: {f.Reason}"));

    // The problems the checker finds in the copy after the mutation, against the plan the copy was not mutated by
    internal static ImmutableArray<CopyProblem> CheckMutatedCopy(
        string method,
        string typeArgument,
        Func<MethodDeclarationSyntax, MethodDeclarationSyntax> mutate,
        Func<GenericCopyPlan, GenericCopyPlan>? mutatePlan = null)
    {
        var compilation = CreateCompilation();
        var type = compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException($"No type '{typeArgument}'.");
        var instance = Instance(compilation, method, type);
        var plan = GenericCopies.Plan(compilation, instance);
        var set = GenericCopies.Create(compilation, [mutatePlan?.Invoke(plan) ?? plan]);
        var copyDeclaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(set.Copies.Single().Declaration);
        var mutated = mutate(copyDeclaration);
        var oldTree = copyDeclaration.SyntaxTree;
        var newTree = oldTree.WithRootAndOptions(oldTree.GetRoot().ReplaceNode(copyDeclaration, mutated),
            oldTree.Options);
        var updatedCompilation = set.Compilation.ReplaceSyntaxTree(oldTree, newTree);
        var updatedCopy = newTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == copyDeclaration.Identifier.ValueText);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(
            instance.Definition.DeclaringSyntaxReferences.Single().GetSyntax());
        return GenericCopyChecker.Check(
            CopyNodeIndex.List(declaration),
            declaration,
            compilation.GetSemanticModel(declaration.SyntaxTree),
            updatedCopy,
            updatedCompilation.GetSemanticModel(newTree),
            plan,
            new InstanceSymbolMap(instance, compilation));
    }

    internal static CSharpCompilation CreateCompilation() =>
        RoslynTestUtility.CreateCompilation(s_source)
            .WithAssemblyName("GenericCopiesTests")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
