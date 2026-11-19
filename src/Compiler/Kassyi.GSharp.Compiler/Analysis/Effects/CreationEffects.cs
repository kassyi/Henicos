namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The effects of creating a value for <see cref="ExpressionEffects"/>: object creation, <c>with</c> expressions and
/// object initializers.
/// </summary>
internal static class CreationEffects
{
    public static void AddObjectCreation(
        ref EffectWalker walker,
        BaseObjectCreationExpressionSyntax creation
    )
    {
        var model = walker.Model;
        if (model.GetTypeSymbol(creation).IsSharedMemoryType())
        {
            walker.Writes.Memory = true;
        }

        if (model.GetSymbol(creation) is IMethodSymbol constructor)
        {
            CallEffects.AddCall(ref walker, constructor, creation.ArgumentList?.Arguments ?? default, receiver: null);
        }
        else if (creation.ArgumentList != null)
        {
            walker.AddArgumentValues(creation.ArgumentList.Arguments);
        }

        if (creation.Initializer != null)
        {
            AddInitializer(ref walker, creation.Initializer);
        }
    }

    // The members that the initializer assigns belong to the copy
    public static void AddWith(ref EffectWalker walker, WithExpressionSyntax with)
    {
        walker.AddValue(with.Expression);
        AddInitializer(ref walker, with.Initializer);
    }

    // The members that an object initializer assigns belong to the new object: their values are evaluated, and the setter
    // of a computed property is called
    private static void AddInitializer(ref EffectWalker walker, InitializerExpressionSyntax initializer)
    {
        foreach (var expression in initializer.Expressions)
        {
            switch (expression)
            {
                case AssignmentExpressionSyntax { Right: InitializerExpressionSyntax nested }:
                    AddInitializer(ref walker, nested);
                    break;
                case AssignmentExpressionSyntax assignment:
                    walker.AddValue(assignment.Right);
                    OperatorEffects.AddAccessorCalls(ref walker, assignment.Left, callsGetter: false);
                    break;
                // An element of a collection initializer: its values, then the call of Add on the new object
                default:
                    walker.AddValue(expression);
                    if (walker.Model.GetCollectionInitializerAdd(expression) is { } add)
                    {
                        CallEffects.AddCallOf(ref walker, add.ReducedFrom ?? add, reference: null);
                    }

                    break;
            }
        }
    }
}
