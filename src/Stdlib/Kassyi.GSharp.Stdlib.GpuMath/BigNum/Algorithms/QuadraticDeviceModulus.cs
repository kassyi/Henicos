namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// The device stage of <see cref="FermatAlgorithm"/>, <c>E &gt;= 32</c>: residues of <c>L</c> words in buffers this
/// instance owns. On the GPU, a square goes through <see cref="KernelAdvancedMathHandler.Square"/> with an owned
/// <see cref="KernelAdvancedMathHandler.NttSquareScratch"/>, a product of two registers through
/// <see cref="KernelAdvancedMathHandler.Multiply"/> with an owned
/// <see cref="KernelAdvancedMathHandler.NttMultiplyScratch"/>, and both are reduced by
/// <c>GpuBigIntFermatKernels.QuadraticFoldReduceKernel</c>, written back into the target register. In CPU fallback mode,
/// products come from <see cref="System.Numerics.BigInteger"/> and are reduced by
/// <see cref="QuadraticFoldModulus.FoldReference"/>.
/// </summary>
/// <remarks>
/// <para>
/// On the GPU, squarings of the state run as CUDA Graphs, one per chain length, captured once (after one eager warm-up
/// squaring for the life of the instance) and replayed until <see cref="Dispose"/>. No operation allocates: the NTT
/// scratch, the product and the fold temporaries are allocated once, with <c>new GpuBigUInt</c>, not from the workspace's
/// temporary pool.
/// </para>
/// <para>The CPU fallback mode is fixed at construction; the caller must not use the instance in the other mode.</para>
/// </remarks>
internal sealed class QuadraticDeviceModulus : ISquaringModulus
{
    // Longest chain captured into one graph; longer requests replay it, the remainder has its own graph.
    private const int MaxGraphLength = 256;

    // QuadraticFoldReduceKernel runs as one block; the passes scan carries per warp, so a full block of 32 warps.
    private const uint FoldBlockSize = 1024;

    private static readonly int s_registerCount = Enum.GetValues<SquaringRegister>().Length;

    private readonly QuadraticFoldModulus _modulus;
    private readonly bool _isCpu;
    private readonly GpuWorkspace _workspace;
    private readonly GpuBigUInt[] _registers = new GpuBigUInt[s_registerCount];
    private readonly GpuBigUInt _product;
    private readonly GpuBigUInt _firstFold;
    private readonly GpuBigUInt _secondFold;
    private readonly KernelAdvancedMathHandler.NttSquareScratch? _squareScratch;
    private readonly KernelAdvancedMathHandler.NttMultiplyScratch? _multiplyScratch;
    private readonly Dictionary<int, (IntPtr Graph, IntPtr GraphExec)> _graphs = [];
    private readonly IGpuGraphProvider _graphProvider;
    private bool _warmedUp;

    /// <summary>Allocates the registers and scratch for <paramref name="modulus"/>.</summary>
    /// <param name="modulus">The modulus (<c>E &gt;= 4</c>, where the fold is defined).</param>
    /// <param name="isCpu">Whether the instance runs in CPU fallback mode.</param>
    /// <param name="graphProvider">The CUDA Graph operations, or <see langword="null"/> for the workspace provider; tests
    /// pass a wrapper to observe them.</param>
    public QuadraticDeviceModulus(QuadraticFoldModulus modulus, bool isCpu, IGpuGraphProvider? graphProvider = null)
    {
        if (modulus.E < QuadraticFoldModulus.MinFoldExponent)
        {
            throw new ArgumentOutOfRangeException(nameof(modulus), modulus.E,
                $"The fold needs E >= {QuadraticFoldModulus.MinFoldExponent}.");
        }

        _modulus = modulus;
        _isCpu = isCpu;
        _workspace = new GpuWorkspace(modulus.ProductWords);
        _graphProvider = graphProvider ?? _workspace.Provider;
        try
        {
            for (var i = 0; i < s_registerCount; i++)
            {
                _registers[i] = new GpuBigUInt(_workspace, modulus.Words);
            }

            _product = new GpuBigUInt(_workspace, modulus.ProductWords);
            if (!isCpu)
            {
                // The fold temporaries are only used by the kernel; FoldReference rents its own on the host.
                _firstFold = new GpuBigUInt(_workspace, modulus.FirstFoldWords);
                _secondFold = new GpuBigUInt(_workspace, modulus.SecondFoldWords);
                var handler = GSharpKernelProvider.Instance.KernelAdvancedMath;
                _squareScratch = KernelAdvancedMathHandler.NttSquareScratch.Allocate(_workspace, handler, modulus.Words);
                _multiplyScratch =
                    KernelAdvancedMathHandler.NttMultiplyScratch.Allocate(_workspace, handler, modulus.ProductWords);
            }
        }
        catch
        {
            // The workspace releases every buffer allocated from it.
            _workspace.Dispose();
            throw;
        }
    }

    public int Words => _modulus.Words;

    /// <summary>
    /// Gets or sets a test hook called before each squaring recorded into a CUDA Graph, with its index; an exception it
    /// throws fails the capture, which must then be ended and its partial graph destroyed. <see langword="null"/> in
    /// production.
    /// </summary>
    internal Action<int>? CaptureFault { get; set; }

    public BigInteger GetModulus() =>
        new(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_modulus.Modulus), isUnsigned: true, isBigEndian: false);

    public bool IsBelowModulus(ReadOnlySpan<uint> words)
    {
        var modulus = _modulus.Modulus;
        for (var i = words.Length - 1; i >= 0; i--)
        {
            if (words[i] != modulus[i])
            {
                return words[i] < modulus[i];
            }
        }

        return false;
    }

    public void Write(SquaringRegister register, ReadOnlySpan<uint> words) =>
        _workspace.Provider.CopyHostToDevice(_registers[(int)register].DevicePointer, words.ToArray(),
            _modulus.Words, _workspace.Stream);

    public uint[] ReadCanonical(SquaringRegister register)
    {
        var words = new uint[_modulus.Words];
        _workspace.Provider.CopyDeviceToHost(words, _registers[(int)register].DevicePointer, _modulus.Words,
            _workspace.Stream);
        _modulus.Canonicalize(words);
        return words;
    }

    public async ValueTask<uint[]> ReadCanonicalAsync(SquaringRegister register, CancellationToken cancellationToken)
    {
        var words = new uint[_modulus.Words];
        await _workspace.Provider
            .CopyDeviceToHostAsync(words, _registers[(int)register].DevicePointer, _modulus.Words, _workspace.Stream,
                cancellationToken)
            .ConfigureAwait(false);
        _modulus.Canonicalize(words);
        return words;
    }

    public void Copy(SquaringRegister destination, SquaringRegister source) =>
        _workspace.Provider.CopyDeviceToDevice(_registers[(int)destination].DevicePointer,
            _registers[(int)source].DevicePointer, _modulus.Words, _workspace.Stream);

    public void SquareState(int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (_isCpu)
        {
            // No graphs on the CPU fallback: every squaring runs directly (#647).
            Square(SquaringRegister.State, count);
            return;
        }

        if (!_warmedUp)
        {
            // One eager squaring loads the kernels before the first capture, which may not load modules.
            SquareOnce(SquaringRegister.State);
            _warmedUp = true;
            count--;
        }

        while (count > 0)
        {
            var length = Math.Min(count, MaxGraphLength);
            var repeats = count / length;
            var graphExec = GetOrCaptureGraph(length);
            for (var r = 0; r < repeats; r++)
            {
                _graphProvider.LaunchGraph(graphExec, _workspace.Stream);
            }

            count -= length * repeats;
        }
    }

    public void Square(SquaringRegister register, int count)
    {
        for (var i = 0; i < count; i++)
        {
            SquareOnce(register);
        }
    }

    public void Multiply(SquaringRegister target, SquaringRegister left, SquaringRegister right)
    {
        var leftBuffer = _registers[(int)left];
        var rightBuffer = _registers[(int)right];
        if (_isCpu)
        {
            _workspace.Provider.Mul(_workspace, leftBuffer, rightBuffer, _product);
        }
        else if (left == right)
        {
            SquareOnDevice(leftBuffer);
        }
        else
        {
            // Not GpuBigIntMath.Mul: its scratch comes from the workspace pool on every call.
            GSharpKernelProvider.Instance.KernelAdvancedMath.Multiply(_workspace, leftBuffer, rightBuffer, _product,
                Required(_multiplyScratch));
        }

        Fold(_registers[(int)target]);
    }

    public void Synchronize() => _workspace.Provider.SynchronizeStream(_workspace.Stream);

    public ValueTask SynchronizeAsync(CancellationToken cancellationToken) =>
        _workspace.Provider.SynchronizeStreamAsync(_workspace.Stream, cancellationToken);

    public void Dispose()
    {
        // Each graph leaves the cache before it is destroyed, so a second Dispose never destroys it again; a failure is
        // reported after everything else is released.
        List<Exception>? errors = null;
        foreach (var length in new List<int>(_graphs.Keys))
        {
            _graphs.Remove(length, out var entry);
            try
            {
                _graphProvider.DestroyGraph(entry.Graph, entry.GraphExec);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        try
        {
            // Written out (not '?.') so that the ownership analyzer (GSH0022) sees the calls.
            if (_squareScratch != null)
            {
                _squareScratch.Dispose();
            }

            if (_multiplyScratch != null)
            {
                _multiplyScratch.Dispose();
            }

            _firstFold.Dispose();
            _secondFold.Dispose();
            _product.Dispose();
            foreach (var register in _registers)
            {
                register.Dispose();
            }
        }
        finally
        {
            _workspace.Dispose();
        }

        if (errors != null)
        {
            throw new AggregateException("Destroying the CUDA Graphs of the squaring chain failed.", errors);
        }
    }

    private void SquareOnce(SquaringRegister register) => Multiply(register, register, register);

    private void SquareOnDevice(GpuBigUInt source) =>
        GSharpKernelProvider.Instance.KernelAdvancedMath.Square(_workspace, source, _product, Required(_squareScratch));

    private static T Required<T>(T? scratch) where T : class =>
        scratch ?? throw new InvalidOperationException("The GPU scratch is not allocated in CPU fallback mode.");

    /// <summary>Reduces the product into <paramref name="residue"/>.</summary>
    private void Fold(GpuBigUInt residue)
    {
        if (_isCpu)
        {
            FoldOnHost(residue);
            return;
        }

        GpuBigIntFermatKernels.LaunchQuadraticFoldReduceKernelPtr(
            new GpuBigIntFermatKernels.AotContexts.QuadraticFold
            {
                Product = _product.DevicePointer,
                FirstFold = _firstFold.DevicePointer,
                SecondFold = _secondFold.DevicePointer,
                Residue = residue.DevicePointer,
                ProductWords = _modulus.ProductWords,
                FirstFoldWords = _modulus.FirstFoldWords,
                SecondFoldWords = _modulus.SecondFoldWords,
                ResidueWords = _modulus.Words,
                ModulusBits = _modulus.ModulusBits,
                Exponent = _modulus.E,
                ConstantTerm = QuadraticFoldModulus.ConstantTerm
            },
            gridDimX: 1, blockDimX: FoldBlockSize, hStream: _workspace.Stream);
    }

    // CPU fallback memory is host memory (Marshal.AllocHGlobal), so the reference fold reads and writes it directly.
    private unsafe void FoldOnHost(GpuBigUInt residue)
    {
        var product = new ReadOnlySpan<uint>((void*)_product.DevicePointer, _modulus.ProductWords);
        var result = new Span<uint>((void*)residue.DevicePointer, _modulus.Words);
        _modulus.FoldReference(product, result);
    }

    /// <summary>Returns the graph that squares the state <paramref name="length"/> times, capturing it on first use.</summary>
    /// <exception cref="InvalidOperationException">The capture produced no graph.</exception>
    private IntPtr GetOrCaptureGraph(int length)
    {
        if (_graphs.TryGetValue(length, out var cached))
        {
            return cached.GraphExec;
        }

        var provider = _graphProvider;
        var stream = _workspace.Stream;
        provider.BeginCapture(stream);
        try
        {
            for (var i = 0; i < length; i++)
            {
                CaptureFault?.Invoke(i);
                SquareOnce(SquaringRegister.State);
            }
        }
        catch (Exception captureError)
        {
            // End the capture so that the stream works again, and release the partial graph, then report the
            // original error (with the cleanup error, if the cleanup fails too).
            try
            {
                provider.DestroyGraph(provider.EndCapture(stream), IntPtr.Zero);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(captureError, cleanupError);
            }

            throw;
        }

        var graph = provider.EndCapture(stream);
        if (graph == IntPtr.Zero)
        {
            // Returning here would silently run fewer squarings than requested (#647).
            throw new InvalidOperationException(
                $"Capturing {length} squarings on the GPU workspace stream produced no CUDA Graph.");
        }

        var graphExec = IntPtr.Zero;
        try
        {
            graphExec = provider.InstantiateGraph(graph);
        }
        catch
        {
            provider.DestroyGraph(graph, graphExec);
            throw;
        }

        _graphs.Add(length, (graph, graphExec));
        return graphExec;
    }
}
