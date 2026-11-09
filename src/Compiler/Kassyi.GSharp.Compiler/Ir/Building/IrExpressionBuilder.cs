using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>Builds the IR of an expression from its <see cref="IOperation"/>.</summary>
/// <remarks>
/// A construct without a node becomes an <see cref="IrOpaqueExpression"/> leaf, with the first reason that applies in
/// the order of <see cref="IrOpaqueReason"/>. A parent whose child is opaque is still built, except where the parent's
/// own shape cannot hold the child (an argument list that does not match the parameters, a struct creation with a
/// nested member initializer).
/// </remarks>
internal static class IrExpressionBuilder
{
    /// <summary>Builds <paramref name="operation"/>, used by its holder as <paramref name="category"/>.</summary>
    public static IrExpression Build(IOperation operation, IrCategory category, IrBuildContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (operation is IInvalidOperation || operation.Type is { TypeKind: TypeKind.Error })
        {
            return Opaque(operation, IrOpaqueReason.Invalid, category, context);
        }

        if (!HasNode(operation))
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedOperation, category, context);
        }

        if (operation.Type is not { } type)
        {
            return Opaque(operation, IrOpaqueReason.Typeless, category, context);
        }

        var source = context.SourceOf(operation);
        return operation switch
        {
            ILiteralOperation => new IrConstant(operation.ConstantValue.Value, null, type, source),
            IFieldReferenceOperation field => BuildField(field, type, category, source, context),
            ILocalReferenceOperation { Local.IsConst: true } local =>
                new IrConstant(operation.ConstantValue.Value, local.Local, type, source),
            ILocalReferenceOperation local => new IrVariableReference(context.Variables.GetLocal(local.Local),
                local.IsDeclaration, category, operation.ConstantValue, source),
            IParameterReferenceOperation parameter => new IrVariableReference(
                context.Variables.GetParameter(parameter.Parameter), false, category, operation.ConstantValue, source),
            IInstanceReferenceOperation instance => BuildInstance(instance, type, category, source, context),
            IArrayElementReferenceOperation element => BuildElement(element, type, category, source, context),
            IPropertyReferenceOperation property => BuildProperty(property, category, source, context),
            IInvocationOperation invocation => BuildInvocation(invocation, type, category, source, context),
            IUnaryOperation unary => BuildUnary(unary, type, category, source, context),
            IBinaryOperation binary => BuildBinary(binary, type, category, source, context),
            IIsPatternOperation pattern => BuildIsPattern(pattern, type, category, source, context),
            IConditionalOperation { WhenFalse: { } whenFalse } conditional =>
                BuildConditional(conditional, whenFalse, type, category, source, context),
            ICoalesceOperation coalesce => BuildCoalesce(coalesce, type, category, source, context),
            IConversionOperation conversion => BuildConversion(conversion, type, category, source, context),
            ISimpleAssignmentOperation assignment => BuildAssignment(assignment, type, category, source, context),
            ICompoundAssignmentOperation compound => BuildCompound(compound, type, category, source, context),
            IIncrementOrDecrementOperation step => BuildIncrement(step, type, category, source, context),
            IObjectCreationOperation creation => IrCreationBuilder.BuildStruct(creation, type, category, source,
                context),
            IArrayCreationOperation array => IrCreationBuilder.BuildArray(array, type, category, source, context),
            IAddressOfOperation address => new IrAddressOf(Build(address.Reference, IrCategory.Location, context), type,
                source),
            IDefaultValueOperation => operation.ConstantValue.HasValue
                ? new IrConstant(operation.ConstantValue.Value, null, type, source)
                : new IrDefaultValue(type, category, source),
            ISizeOfOperation or INameOfOperation => operation.ConstantValue.HasValue
                ? new IrConstant(operation.ConstantValue.Value, null, type, source)
                : Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context),
            IDiscardOperation => new IrDiscard(type, source),
            IDeclarationExpressionOperation { Expression: ILocalReferenceOperation declared } =>
                Build(declared, category, context),
            _ => Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context)
        };
    }

    /// <summary>Whether the kind of <paramref name="operation"/> has a node (its shape may still be
    /// unsupported).</summary>
    public static bool HasNode(IOperation operation) => operation is ILiteralOperation or IFieldReferenceOperation
        or ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation
        or IArrayElementReferenceOperation or IPropertyReferenceOperation or IInvocationOperation or IUnaryOperation
        or IBinaryOperation or IIsPatternOperation or IConditionalOperation or ICoalesceOperation
        or IConversionOperation or ISimpleAssignmentOperation or ICompoundAssignmentOperation
        or IIncrementOrDecrementOperation or IObjectCreationOperation or IArrayCreationOperation
        or IAddressOfOperation or IDefaultValueOperation or ISizeOfOperation or INameOfOperation or IDiscardOperation
        or IDeclarationExpressionOperation;

    /// <summary>An opaque leaf for <paramref name="operation"/>.</summary>
    public static IrOpaqueExpression Opaque(IOperation operation, IrOpaqueReason reason, IrCategory category,
        IrBuildContext context) =>
        new(operation.Syntax, operation, reason, operation.Type ?? context.VoidType, category,
            context.SourceOf(operation));

    /// <summary>
    /// Whether <paramref name="operation"/> is the <see langword="null"/> literal or a conversion of it (a null pointer
    /// is not a C# constant, so its conversion has no constant value).
    /// </summary>
    public static bool IsNullConstant(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: null } ||
        operation is IConversionOperation { Operand: { Type: null, ConstantValue: { HasValue: true, Value: null } } };

    private static IrExpression BuildField(IFieldReferenceOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        var field = operation.Field;
        if (field.IsConst)
        {
            return new IrConstant(operation.ConstantValue.Value, field, type, source);
        }

        if (field.IsFixedSizeBuffer)
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var receiver = operation.Instance is { } instance
            ? Build(instance, IrCategories.ForFieldReceiver(instance, category), context)
            : null;
        return new IrFieldReference(receiver, field, category, operation.ConstantValue, source);
    }

    private static IrExpression BuildInstance(IInstanceReferenceOperation operation, ITypeSymbol type,
        IrCategory category, IrSource source, IrBuildContext context) => operation.ReferenceKind switch
    {
        InstanceReferenceKind.ContainingTypeInstance when context.This is { } @this =>
            new IrVariableReference(@this, false, category, default, source),
        InstanceReferenceKind.ImplicitReceiver => new IrInitializedObject(type, source),
        _ => Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context)
    };

    private static IrExpression BuildElement(IArrayElementReferenceOperation operation, ITypeSymbol type,
        IrCategory category, IrSource source, IrBuildContext context)
    {
        if (operation.Indices is not [var index])
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var array = Build(operation.ArrayReference, IrCategories.ForRead(operation.ArrayReference.Type), context);
        return new IrElementReference(array, Build(index, IrCategories.ForRead(index.Type), context), type, category,
            source);
    }

    private static IrExpression BuildProperty(IPropertyReferenceOperation operation, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        var property = operation.Property;
        IrExpression? receiver = null;
        if (operation.Instance is { } instance)
        {
            var accessor = IrCategories.AccessorOf(property, category);
            receiver = Build(instance, IrCategories.ForReceiver(instance, accessor), context);
        }

        if (!IrArgumentBuilder.TryBuild(operation.Arguments, property.Parameters, context, out var arguments,
                out var failure))
        {
            return Opaque(operation, failure, category, context);
        }

        return new IrPropertyReference(receiver, property, arguments, category, operation.ConstantValue, source);
    }

    private static IrExpression BuildInvocation(IInvocationOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        var method = operation.TargetMethod;
        if (method.MethodKind is MethodKind.DelegateInvoke or MethodKind.LocalFunction)
        {
            // Invoke names no device function, and a local function stays with its opaque declaration
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var receiver = operation.Instance is { } instance
            ? Build(instance, IrCategories.ForReceiver(instance, method), context)
            : null;
        if (!IrArgumentBuilder.TryBuild(operation.Arguments, method.Parameters, context, out var arguments,
                out var failure))
        {
            return Opaque(operation, failure, category, context);
        }

        return new IrCall(method, receiver, arguments, type, category, operation.ConstantValue, source);
    }

    private static IrExpression BuildUnary(IUnaryOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        if (operation.IsLifted)
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var operand = Build(operation.Operand, IrCategories.ForRead(operation.Operand.Type), context);
        var @operator = new IrOperator<UnaryOperatorKind>(operation.OperatorKind, operation.OperatorMethod,
            operation.IsChecked);
        return new IrUnary(@operator, operand, type, category, operation.ConstantValue, source);
    }

    private static IrExpression BuildBinary(IBinaryOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        var kind = operation.OperatorKind;
        if (kind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
        {
            if (operation.OperatorMethod is not null)
            {
                // && and || through operator true / false and a user-defined & or |
                return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
            }

            return new IrLogical(kind == BinaryOperatorKind.ConditionalAnd, BuildRead(operation.LeftOperand, context),
                BuildRead(operation.RightOperand, context), type, category, operation.ConstantValue, source);
        }

        if (kind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals && operation.OperatorMethod is null &&
            TryGetNullTestOperand(operation, out var tested))
        {
            return new IrNullTest(BuildRead(tested, context), kind == BinaryOperatorKind.NotEquals, type, category,
                source);
        }

        if (operation.IsLifted)
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var @operator = new IrOperator<BinaryOperatorKind>(kind, operation.OperatorMethod, operation.IsChecked);
        return new IrBinary(@operator, BuildRead(operation.LeftOperand, context),
            BuildRead(operation.RightOperand, context), type, category, operation.ConstantValue, source);
    }

    // x == null or null == x, with null on one side only. The operand is the value as written: C# compares an array
    // with null as an object, through an implicit reference conversion that the test does not need
    private static bool TryGetNullTestOperand(IBinaryOperation operation, [NotNullWhen(true)] out IOperation? tested)
    {
        var leftIsNull = IsNullConstant(operation.LeftOperand);
        var rightIsNull = IsNullConstant(operation.RightOperand);
        tested = (leftIsNull, rightIsNull) switch
        {
            (false, true) => operation.LeftOperand,
            (true, false) => operation.RightOperand,
            _ => null
        };
        if (tested is IConversionOperation { IsImplicit: true, Conversion.IsReference: true } conversion)
        {
            tested = conversion.Operand;
        }

        return tested is not null;
    }

    private static IrExpression BuildIsPattern(IIsPatternOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        var (isNullPattern, isNegated) = operation.Pattern switch
        {
            IConstantPatternOperation constant when IsNullConstant(constant.Value) => (true, false),
            INegatedPatternOperation { Pattern: IConstantPatternOperation constant } when IsNullConstant(constant.Value)
                => (true, true),
            _ => (false, false)
        };
        return isNullPattern
            ? new IrNullTest(BuildRead(operation.Value, context), isNegated, type, category, source)
            : Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
    }

    private static IrExpression BuildConditional(IConditionalOperation operation, IOperation whenFalse,
        ITypeSymbol type, IrCategory category, IrSource source, IrBuildContext context)
    {
        // The branches of a ref conditional are the location the conditional is used as
        var branchCategory = operation.IsRef ? category : IrCategories.ForRead(type);
        return new IrConditional(BuildRead(operation.Condition, context),
            Build(operation.WhenTrue, branchCategory, context), Build(whenFalse, branchCategory, context),
            operation.IsRef, category, operation.ConstantValue, source);
    }

    private static IrExpression BuildCoalesce(ICoalesceOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        var value = operation.Value;
        var conversion = IrConversionKinds.Of(operation.ValueConversion, value.Type, type,
            value.ConstantValue.HasValue);
        return new IrCoalesce(BuildRead(value, context), BuildRead(operation.WhenNull, context), conversion, type,
            category, source);
    }

    private static IrExpression BuildConversion(IConversionOperation operation, ITypeSymbol type, IrCategory category,
        IrSource source, IrBuildContext context)
    {
        if (operation.IsTryCast)
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var operand = operation.Operand;
        if (operand.Type is null && operand.ConstantValue.HasValue)
        {
            // The null and default literals have no type of their own: the constant takes the type converted to
            return new IrConstant(operand.ConstantValue.Value, null, type, source);
        }

        var kind = IrConversionKinds.Of(operation.Conversion, operand.Type, type, operand.ConstantValue.HasValue);
        var conversion = new IrOperator<IrConversionKind>(kind, operation.OperatorMethod, operation.IsChecked);
        return new IrConversion(BuildRead(operand, context), conversion, operation.IsImplicit, type, category,
            operation.ConstantValue, source);
    }

    private static IrExpression BuildAssignment(ISimpleAssignmentOperation operation, ITypeSymbol type,
        IrCategory category, IrSource source, IrBuildContext context)
    {
        var target = Build(operation.Target, IrCategory.Location, context);
        var value = operation.IsRef
            ? Build(operation.Value, IrCategories.ForReference(RefKindOf(operation.Target)), context)
            : BuildRead(operation.Value, context);
        return new IrAssignment(target, value, operation.IsRef, type, category, source);
    }

    // The ref kind of the local or parameter a ref assignment rebinds
    private static RefKind RefKindOf(IOperation target) => target switch
    {
        ILocalReferenceOperation local => local.Local.RefKind,
        IParameterReferenceOperation parameter => parameter.Parameter.RefKind,
        IFieldReferenceOperation field => field.Field.RefKind,
        _ => RefKind.Ref
    };

    private static IrExpression BuildCompound(ICompoundAssignmentOperation operation, ITypeSymbol type,
        IrCategory category, IrSource source, IrBuildContext context)
    {
        if (operation.IsLifted)
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        var targetType = operation.Target.Type;
        var inConversion = IrConversionKinds.Of(operation.InConversion, targetType, null, false);
        var outConversion = IrConversionKinds.Of(operation.OutConversion, null, targetType, false);
        var @operator = new IrOperator<BinaryOperatorKind>(operation.OperatorKind, operation.OperatorMethod,
            operation.IsChecked);
        return new IrCompoundAssignment(@operator, Build(operation.Target, IrCategory.Location, context),
            BuildRead(operation.Value, context), inConversion, outConversion, category, source);
    }

    private static IrExpression BuildIncrement(IIncrementOrDecrementOperation operation, ITypeSymbol type,
        IrCategory category, IrSource source, IrBuildContext context)
    {
        if (operation.IsLifted)
        {
            return Opaque(operation, IrOpaqueReason.UnsupportedShape, category, context);
        }

        return new IrIncrement(Build(operation.Target, IrCategory.Location, context), !operation.IsPostfix,
            operation.Kind == OperationKind.Decrement, operation.OperatorMethod, operation.IsChecked, category, source);
    }

    /// <summary>Builds an operand that is read.</summary>
    public static IrExpression BuildRead(IOperation operation, IrBuildContext context) =>
        Build(operation, IrCategories.ForRead(operation.Type), context);
}
