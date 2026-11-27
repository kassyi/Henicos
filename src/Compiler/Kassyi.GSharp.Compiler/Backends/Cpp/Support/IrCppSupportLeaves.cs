using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Backends.Cpp.Support;

/// <summary>The rows of the C++ writer for the leaves: constants and variables.</summary>
internal static class IrCppSupportLeaves
{
    private const string NonDeviceValue = "NonDeviceValue";

    /// <summary>
    /// Whether <paramref name="node"/> is a leaf and, when it is, whether the C++ writer spells it on
    /// <paramref name="profile"/>.
    /// </summary>
    public static bool TryOf(IrNode node, IrNode? parent, CppBodyProfile profile, out IrSupport support)
    {
        IrSupport? row = node switch
        {
            IrConstant constant => OfConstant(constant),
            IrVariableReference reference => OfReference(reference),
            // The legacy route spells a default by its position (a braced list or a typed value); the IR has no plan
            // of that position yet
            IrDefaultValue => IrSupport.Unspelled("Default"),
            // A discard is part of the spelling of an assignment target or an out argument, never a value of its own
            IrDiscard => IrSupport.Unspelled("Discard"),
            IrRuntimeCall => IrSupport.Unspelled("NotInBody"),
            IrOpaqueExpression => IrSupport.Unspelled("Opaque"),
            _ => null
        };
        support = row.GetValueOrDefault();
        return row.HasValue;
    }

    private static IrSupport OfConstant(IrConstant constant)
    {
        var type = constant.Type;
        if (type.UnwrapNullable().SpecialType is SpecialType.System_Decimal or SpecialType.System_String)
        {
            return IrSupport.Unspelled(NonDeviceValue);
        }

        if (IrCppSupportTypes.UnspellableShape(type) is { } shape)
        {
            return IrCppSupportTypes.Unspelled(shape);
        }

        // A constant default, default(T), sizeof or a typeless default converted to a type: the legacy route spells
        // it by its position ({} or a typed value) or as written, not as its value
        if (constant is { LiteralText: null, Origin: null, Value: not null })
        {
            return IrSupport.Unspelled("Unwritten");
        }

        if (constant.Value is not { } value)
        {
            return IrSupport.Spelled("Null");
        }

        if (constant.LiteralText is not null && CppLiteralWriter.GetLiteralType(value) != SpecialType.None)
        {
            return IrSupport.Spelled("Literal");
        }

        return ConstexprLiteral.TryFormatConstant(type, value, out _)
            ? IrSupport.Spelled("Value")
            : IrSupport.Unspelled(NonDeviceValue);
    }

    private static IrSupport OfReference(IrVariableReference reference)
    {
        var variable = reference.Variable;
        if (variable.Kind == IrVariableKind.This)
        {
            return IrSupport.Unspelled("This");
        }

        if (reference.IsDeclaration)
        {
            return IrSupport.Unspelled("Declaration");
        }

        if (variable.RefKind != RefKind.None)
        {
            return IrSupport.Unspelled("Ref");
        }

        if (IrCppSupportTypes.UnspellableShape(variable.Type) is { } shape)
        {
            return IrCppSupportTypes.Unspelled(shape);
        }

        return variable.Kind switch
        {
            IrVariableKind.Local => IrSupport.Spelled("Local"),
            IrVariableKind.Parameter => IrSupport.Spelled("Parameter"),
            IrVariableKind.Temporary => IrSupport.Spelled("Temporary"),
            _ => throw new InvalidOperationException("A variable is a local, a parameter, this or a temporary.")
        };
    }
}
