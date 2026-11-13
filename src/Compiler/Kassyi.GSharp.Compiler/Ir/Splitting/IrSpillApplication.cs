namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// What <see cref="IrSpillRewriter.Apply"/> made of a plan: the rewritten statement, or why it was skipped.
/// </summary>
internal readonly struct IrSpillApplication
{
    private readonly IrLoweredStatement? _lowered;

    private IrSpillApplication(IrLoweredStatement? lowered, IrSpillApplySkip skip)
    {
        _lowered = lowered;
        Skip = skip;
    }

    /// <summary>Why the statement was skipped; <see cref="IrSpillApplySkip.None"/> when it was rewritten.</summary>
    public IrSpillApplySkip Skip { get; }

    /// <summary>The rewritten statement.</summary>
    public static IrSpillApplication Lowered(IrLoweredStatement lowered) => new(lowered, IrSpillApplySkip.None);

    /// <summary>A statement left to the syntax-based lowering.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="skip"/> is <see cref="IrSpillApplySkip.None"/>.
    /// </exception>
    public static IrSpillApplication Skipped(IrSpillApplySkip skip) => skip == IrSpillApplySkip.None
        ? throw new ArgumentOutOfRangeException(nameof(skip), "A skipped statement names its reason.")
        : new IrSpillApplication(null, skip);

    /// <summary>The rewritten statement, when there is one.</summary>
    public bool TryGetLowered([NotNullWhen(true)] out IrLoweredStatement? lowered)
    {
        lowered = _lowered;
        return lowered is not null;
    }
}
