namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Specifies the type of comparison operation between GPU vector elements.
/// </summary>
public enum VectorCompareOperator
{
    /// <summary>Tests for equality (==).</summary>
    Equal = 0,

    /// <summary>Tests for inequality (!=).</summary>
    NotEqual = 1,

    /// <summary>Tests for strictly greater than (&gt;).</summary>
    GreaterThan = 2,

    /// <summary>Tests for greater than or equal (&gt;=).</summary>
    GreaterThanOrEqual = 3,

    /// <summary>Tests for strictly less than (&lt;).</summary>
    LessThan = 4,

    /// <summary>Tests for less than or equal (&lt;=).</summary>
    LessThanOrEqual = 5
}
