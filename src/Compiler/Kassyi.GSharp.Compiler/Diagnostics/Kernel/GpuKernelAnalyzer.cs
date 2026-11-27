using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Compiler.Targets;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>Roslyn analyzer that validates C# syntax rules inside GPU kernel and device methods.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuKernelAnalyzer : DiagnosticAnalyzer
{
    // The kind of the type of a C# 14 extension block, which the Roslyn API of the analyzer does not name yet
    private const TypeKind ExtensionTypeKind = (TypeKind)14;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        GSharpDiagnostics.Reported;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var globalOptions = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            // The checks that depend on the dialect see the one the generator emits for
            var dialect = TranspilerDialects.Create(TranspilerDialects.GetName(globalOptions));
            // Exported unless the project turns it off, as the generator reads it
            var exports = !(globalOptions.TryGetValue("build_property.GSharpExportDeviceCode", out var export) &&
                            string.Equals(export, "false", StringComparison.OrdinalIgnoreCase));
            var bothOnStacks = NativeBarrierModels.Read(globalOptions) == NativeBarrierModel.Stack &&
                               ManagedBarrierModels.Read(globalOptions) == ManagedBarrierModel.Stack;
            // The launch limits of the target GPUs, resolved once per compilation from the MSBuild property
            globalOptions.TryGetValue("build_property.GSharpTargetArchitectures", out var architectures);
            var targetLimits = GpuArchitectureLimits.Resolve(architectures);
            // One GSH0036 per type declaration, however many GPU methods it holds or encloses
            var reportedTypes = new ConcurrentDictionary<TypeDeclarationSyntax, byte>();
            // One diagnostic per ID and location outside the GPU method that found it: the body of a struct member
            // that several GPU methods write
            var reportedOutside = new ConcurrentDictionary<(string Id, Location Location), byte>();
            start.RegisterSyntaxNodeAction(
                context => AnalyzeMethod(context, dialect, exports, targetLimits, bothOnStacks, reportedTypes,
                    reportedOutside),
                SyntaxKind.MethodDeclaration);
            // An operator or an accessor that is GPU code is a method of its type as well: that type is reopened too
            start.RegisterSyntaxNodeAction(context => AnalyzeOtherMember(context, reportedTypes),
                SyntaxKind.OperatorDeclaration,
                SyntaxKind.ConversionOperatorDeclaration,
                SyntaxKind.GetAccessorDeclaration,
                SyntaxKind.SetAccessorDeclaration,
                SyntaxKind.InitAccessorDeclaration,
                SyntaxKind.AddAccessorDeclaration,
                SyntaxKind.RemoveAccessorDeclaration);
            start.RegisterSyntaxNodeAction(
                context => AnalyzeDestructor(context, dialect, exports, targetLimits, bothOnStacks),
                SyntaxKind.DestructorDeclaration);
        });
    }

    // A finalizer marked as GPU code is reported by its rule (GpuFinalizerRule); the generator does not emit it
    private static void AnalyzeDestructor(SyntaxNodeAnalysisContext context, ITranspilerDialect dialect, bool exports,
        in TargetLimits targetLimits, bool bothOnStacks)
    {
        var destructor = (DestructorDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(destructor) is not { } symbol ||
            !GpuExecutionSpace.IsGpuFunction(symbol))
        {
            return;
        }

        var validationContext = new ValidationContext(context.SemanticModel, symbol, context.ReportDiagnostic, dialect,
            exports, targetLimits, bothOnStacks);
        using var walker = GpuKernelValidationWalker.Rent(validationContext);
        walker.Visit(destructor);
    }

    // A class, struct or record: the generator reopens it with a partial declaration (the managed version of a device
    // function is written there). An interface is not reopened.
    private static bool IsReopenedDeclaration(SyntaxNode? node) =>
        node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax;

    // An operator or an accessor marked as GPU code: only the types that hold it must be partial
    private static void AnalyzeOtherMember(SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<TypeDeclarationSyntax, byte> reportedTypes)
    {
        if (context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(context.Node) is { } symbol &&
            GpuExecutionSpace.IsGpuFunction(symbol))
        {
            ReportNonPartialTypes(context, context.Node, symbol, reportedTypes);
        }
    }

    // The generator adds members to the type that holds GPU code (the managed version of a device function, the
    // launch wrappers), so that type and every type enclosing it must be partial (#941, #1897). The type of an
    // accessor is the one around its property, indexer or event; an extension block cannot be partial, so the
    // class around it is the one that is reopened.
    private static void ReportNonPartialTypes(SyntaxNodeAnalysisContext context, SyntaxNode member,
        IMethodSymbol symbol, ConcurrentDictionary<TypeDeclarationSyntax, byte> reportedTypes)
    {
        var holder = member.Parent;
        while (holder is AccessorListSyntax or BasePropertyDeclarationSyntax)
        {
            holder = holder.Parent;
        }

        if (symbol.ContainingType?.TypeKind == ExtensionTypeKind && holder is TypeDeclarationSyntax block)
        {
            holder = block.Parent;
        }

        if (!IsReopenedDeclaration(holder))
        {
            return;
        }

        for (var parent = holder; parent is TypeDeclarationSyntax typeDecl; parent = parent.Parent)
        {
            if (!typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword) && reportedTypes.TryAdd(typeDecl, 0))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.KernelClassMustBePartial,
                    typeDecl.Identifier.GetLocation(),
                    typeDecl.Identifier.Text);
            }
        }
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, ITranspilerDialect dialect, bool exports,
        in TargetLimits targetLimits, bool bothOnStacks, ConcurrentDictionary<TypeDeclarationSyntax, byte> reportedTypes,
        ConcurrentDictionary<(string Id, Location Location), byte> reportedOutside)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDeclaration);
        if (methodSymbol == null)
        {
            return;
        }

        if (!GpuExecutionSpace.IsGpuFunction(methodSymbol))
        {
            return;
        }

        ReportNonPartialTypes(context, methodDeclaration, methodSymbol, reportedTypes);

        // The generator transpiles the defining declaration of a partial method from its implementing declaration,
        // which this analyzer visits on its own. A device function that is a partial method without an implementing
        // declaration needs nothing either: C# removes its calls, and so does G#. An extern method or a kernel with no
        // body has nothing to transpile or launch.
        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
        {
            if (methodSymbol.PartialImplementationPart == null &&
                !(methodSymbol.IsPartialDefinition && !methodSymbol.HasAttribute(KnownAttributes.GpuKernel)))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    methodDeclaration.Identifier.GetLocation(),
                    $"GPU function '{methodSymbol.Name}' without a body " +
                    "(an extern method, or a partial kernel without an implementing declaration)");
            }

            return;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // Single-pass traversal of the AST to minimize time complexity and zero allocations
        var validationContext = new ValidationContext(context.SemanticModel, methodSymbol, diagnostics.Add, dialect,
            exports, targetLimits, bothOnStacks);
        using (var walker = GpuKernelValidationWalker.Rent(validationContext))
        {
            walker.Visit(methodDeclaration);
        }

        AddDeclarationFailures(methodDeclaration, methodSymbol, context.SemanticModel, diagnostics);

        // A rule and the transpiler, or the signature and the body, find the same construct: one diagnostic per ID and location
        var reported = new HashSet<(string Id, Location Location)>();
        diagnostics
            .AsStackEnumerable()
            .Where(reported, static (diagnostic, seen) => seen.Add((diagnostic.Id, diagnostic.Location)))
            .Where(
                (methodDeclaration, reportedOutside),
                static (diagnostic, state) => !IsOutside(diagnostic.Location, state.methodDeclaration) ||
                                              state.reportedOutside.TryAdd((diagnostic.Id, diagnostic.Location), 0)
            ).Do(context, static (diagnostic, analysis) => analysis.ReportDiagnostic(diagnostic));
    }

    // What the generator could not emit for the declaration. It does not transpile a generic device function that uses
    // its type parameters (GenericDefinitions): one diagnostic at its name says so, instead of one for each use of a
    // type parameter that the transpiler meets
    private static void AddDeclarationFailures(MethodDeclarationSyntax declaration, IMethodSymbol method,
        SemanticModel model, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
            GenericDefinitions.IsLeftOut(method, model.Compilation))
        {
            diagnostics.Add(Diagnostic.Create(
                GSharpDiagnostics.GenericDeviceFunctionNotCompiled,
                declaration.Identifier.GetLocation(),
                method.Name,
                GenericDefinitions.NotSupportedReason));
            return;
        }

        TranspilerFailureCheck.AddFailures(declaration, method, model, diagnostics);
    }

    // A location in source that the declaration does not contain; a diagnostic without a location stays with its method
    private static bool IsOutside(Location location, SyntaxNode declaration) =>
        location.IsInSource &&
        (location.SourceTree != declaration.SyntaxTree || !declaration.FullSpan.Contains(location.SourceSpan));
}
