namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Defines an arithmetic operator capable of executing operations on arbitrary-precision float operands via static abstract polymorphism.
/// </summary>
/// <typeparam name="T">The floating-point operand type (<see cref="GpuBigFloat"/> or <see cref="GpuBigUFloat"/>).</typeparam>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Employs C# generic math static abstract methods to achieve zero-overhead operator abstraction
/// without runtime delegate allocation or virtual dispatch overhead.
/// </para>
/// </remarks>
public interface IFloatOperator<T>
{
    /// <summary>
    /// Executes the specified arithmetic operator using the provided float expression visitor.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="visitor">The expression visitor responsible for evaluating intermediate nodes.</param>
    /// <returns>The evaluated floating-point result.</returns>
    static abstract T Execute(T left, T right, ref GpuBigFloatExpressionVisitor visitor);
}
