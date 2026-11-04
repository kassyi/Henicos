namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Represents the error code recorded by the G# device or native CPU runtime error channel.
/// </summary>
public enum GSharpErrorCode
{
    /// <summary>No error occurred.</summary>
    None = 0,

    /// <summary>Corresponds to <see cref="NotSupportedException"/>.</summary>
    NotSupported = 1,

    /// <summary>Corresponds to <see cref="InvalidOperationException"/>.</summary>
    InvalidOperation = 2,

    /// <summary>Corresponds to <see cref="IndexOutOfRangeException"/>.</summary>
    IndexOutOfRange = 3,

    /// <summary>Corresponds to <see cref="ArgumentOutOfRangeException"/>.</summary>
    ArgumentOutOfRange = 4,

    /// <summary>Corresponds to <see cref="ArgumentException"/>.</summary>
    Argument = 5,

    /// <summary>Corresponds to <see cref="NullReferenceException"/>.</summary>
    NullReference = 6,

    /// <summary>Corresponds to <see cref="DivideByZeroException"/>.</summary>
    DivideByZero = 7,

    /// <summary>Corresponds to <see cref="OverflowException"/>.</summary>
    Overflow = 8,

    /// <summary>Corresponds to <see cref="InvalidOperationException"/> for detected shared-memory data races.</summary>
    DataRace = 9
}

/// <summary>
/// Represents a 1-based or deterministic 31-bit error site recorded by the transpiler (#729, #1056).
/// </summary>
/// <param name="SiteId">The deterministic 31-bit site ID.</param>
/// <param name="File">The source file name where the trap expression was evaluated.</param>
/// <param name="Line">The 1-based source line number.</param>
/// <param name="Column">The 1-based source column number.</param>
/// <param name="MemberName">The enclosing kernel or device method name.</param>
/// <param name="DefaultMessage">The base diagnostic message for this site.</param>
public readonly record struct GSharpErrorSite(
    uint SiteId,
    string File,
    int Line,
    int Column,
    string MemberName,
    string DefaultMessage);

/// <summary>
/// Represents the 24-byte pinned host-mapped device error slot shared with CUDA modules (#729, #1056).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GSharpDeviceErrorSlot
{
    /// <summary>
    /// Low 16 bits: <see cref="GSharpErrorCode"/>; bit 16 (<c>0x10000u</c>): payload write completion flag.
    /// </summary>
    public uint Code;

    /// <summary>
    /// Local compile-time site ID (1-based), or builtin prelude message ID when high bit is set (<c>0x80000000u</c>).
    /// </summary>
    public uint SiteId;

    /// <summary>
    /// Explicit 1-based module ID assigned to the containing CUDA module or dynamic library (#1056 item 5).
    /// </summary>
    public uint ModuleId;

    /// <summary>
    /// Packed thread coordinates: <c>(threadIdx.x &amp; 0x3FFu) | ((threadIdx.y &amp; 0x3FFu) &lt;&lt; 10) | ((threadIdx.z &amp; 0x3FFu) &lt;&lt; 20)</c>.
    /// </summary>
    public uint ThreadLinear;

    /// <summary>
    /// Block X coordinate (<c>blockIdx.x</c>). Preserves large grid coordinates up to 2^31 - 1 (#1056 item 2).
    /// </summary>
    public uint BlockX;

    /// <summary>
    /// Packed Block Y and Z coordinates: <c>(blockIdx.y &amp; 0xFFFFu) | ((blockIdx.z &amp; 0xFFFFu) &lt;&lt; 16)</c>.
    /// </summary>
    public uint BlockYZ;
}

/// <summary>
/// Maps device and native CPU runtime error records to standard .NET exceptions with source location and thread coordinate metadata.
/// </summary>
public static class GSharpErrorMapper
{
    /// <summary>Completion bit set in <see cref="GSharpDeviceErrorSlot.Code"/> after payload fields are written.</summary>
    public const uint CompletionBit = 0x10000u;

    /// <summary>Bit mask for extracting the <see cref="GSharpErrorCode"/> from <see cref="GSharpDeviceErrorSlot.Code"/>.</summary>
    public const uint ErrorCodeMask = 0xFFFFu;

    /// <summary>High bit indicating a builtin prelude error message ID.</summary>
    public const uint BuiltinSiteFlag = 0x80000000u;

    private static int s_nextModuleId;
    private static readonly ConcurrentDictionary<uint, ConcurrentDictionary<uint, GSharpErrorSite>> s_moduleSites = new();
    private static readonly ConcurrentDictionary<string, uint> s_registeredPathIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<uint, string> s_builtinMessages = CreateBuiltinMessages();

    private static ConcurrentDictionary<uint, string> CreateBuiltinMessages()
    {
        var dict = new ConcurrentDictionary<uint, string>();

        // Fixed builtin IDs (0x80000001u .. 0x8000000Du)
        dict[0x80000001u] = "Index was out of range.";
        dict[0x80000002u] = "Sequence contains no elements";
        dict[0x80000003u] = "Sequence contains no matching element";
        dict[0x80000004u] = "Sequence contains more than one element";
        dict[0x80000005u] = "Sequence contains more than one matching element";
        dict[0x80000006u] = "G#: a shared memory declaration grew after a later declaration";
        dict[0x80000007u] = "G#: dynamic shared memory exceeds sharedMemBytes";
        dict[0x80000008u] = "Unable to sort because the IComparer.Compare() method returns inconsistent results.";
        dict[0x80000009u] = "Length of items must be same as length of keys.";
        dict[0x8000000Au] = "Failed to compare two elements in the array.";
        dict[0x8000000Bu] = "G#: a null array is read";
        dict[0x8000000Cu] = "mbarrier is not supported by the StandardCpp dialect.";
        dict[0x8000000Du] = "WMMA is not supported by the StandardCpp dialect.";

        foreach (var pair in dict)
        {
            dict[ComputeBuiltinMessageId(pair.Value)] = pair.Value;
        }

        dict[ComputeBuiltinMessageId("G#: the dynamic shared memory of a block (sharedMemBytes) could not be allocated")] =
            "G#: the dynamic shared memory of a block (sharedMemBytes) could not be allocated";
        dict[ComputeBuiltinMessageId("G#: the static shared memory of a declaration could not be allocated")] =
            "G#: the static shared memory of a declaration could not be allocated";

        return dict;
    }

    /// <summary>
    /// Computes the deterministic FNV-1a 32-bit builtin site ID (with <see cref="BuiltinSiteFlag"/> set) for a message literal.
    /// </summary>
    /// <param name="message">The ASCII/UTF-8 message string.</param>
    /// <returns>The builtin site ID with bit 31 set.</returns>
    public static uint ComputeBuiltinMessageId(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var hash = 2166136261u;
        for (var i = 0; i < message.Length; i++)
        {
            hash ^= (byte)message[i];
            hash *= 16777619u;
        }

        var low = hash & 0x7FFFFFFFu;
        if (low == 0u)
        {
            low = 1u;
        }

        return BuiltinSiteFlag | low;
    }

    /// <summary>
    /// Registers a message string in the builtin message table so that its FNV-1a 32-bit site ID can be resolved at runtime.
    /// </summary>
    /// <param name="message">The message string to register.</param>
    /// <returns>The computed builtin site ID.</returns>
    public static uint RegisterBuiltinMessage(string message)
    {
        var id = ComputeBuiltinMessageId(message);
        s_builtinMessages.TryAdd(id, message);
        return id;
    }

    /// <summary>
    /// Registers an error site table for a named module or DLL key and returns its 1-based module ID.
    /// </summary>
    /// <param name="moduleKey">A unique key identifying the module or native library.</param>
    /// <param name="sites">The error site table.</param>
    /// <returns>The assigned 1-based module ID.</returns>
    public static uint RegisterErrorSites(string moduleKey, ReadOnlySpan<GSharpErrorSite> sites)
    {
        ArgumentNullException.ThrowIfNull(moduleKey);
        var moduleId = s_registeredPathIds.GetOrAdd(
            moduleKey,
            static _ => (uint)Interlocked.Increment(ref s_nextModuleId));

        RegisterModuleErrorSites(moduleId, sites);
        return moduleId;
    }

    /// <summary>
    /// Associates an explicit 1-based module ID with an error site table.
    /// </summary>
    /// <param name="moduleId">The 1-based module ID written to <c>__gsharp_error_module_id</c>.</param>
    /// <param name="sites">The error site table.</param>
    public static void RegisterModuleErrorSites(uint moduleId, ReadOnlySpan<GSharpErrorSite> sites)
    {
        for (var i = 0; i < sites.Length; i++)
        {
            if (!string.IsNullOrEmpty(sites[i].DefaultMessage))
            {
                RegisterBuiltinMessage(sites[i].DefaultMessage);
            }
        }

        var table = s_moduleSites.GetOrAdd(moduleId, static _ => new ConcurrentDictionary<uint, GSharpErrorSite>());
        for (var i = 0; i < sites.Length; i++)
        {
            table[sites[i].SiteId] = sites[i];
        }
    }

    /// <summary>
    /// Allocates a new 1-based module ID for a loaded CUDA module.
    /// </summary>
    /// <returns>A unique 1-based module ID.</returns>
    internal static uint AllocateModuleId()
    {
        var id = (uint)Interlocked.Increment(ref s_nextModuleId);
        if (id == 0u || id == uint.MaxValue)
        {
            throw new InvalidOperationException("G# error module ID space exhausted.");
        }
        return id;
    }

    /// <summary>
    /// Attempts to resolve a site ID to its registered <see cref="GSharpErrorSite"/>.
    /// </summary>
    /// <param name="siteId">The site ID recorded in the error slot.</param>
    /// <param name="site">When this method returns <see langword="true"/>, contains the resolved error site.</param>
    /// <returns><see langword="true"/> if the site was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetSite(uint siteId, out GSharpErrorSite site) =>
        TryGetSite(siteId, 0u, out site);

    /// <summary>
    /// Attempts to resolve a site ID and module ID to its registered <see cref="GSharpErrorSite"/>.
    /// </summary>
    /// <param name="siteId">The site ID recorded in the error slot.</param>
    /// <param name="moduleId">The 1-based module ID.</param>
    /// <param name="site">When this method returns <see langword="true"/>, contains the resolved error site.</param>
    /// <returns><see langword="true"/> if the site was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetSite(uint siteId, uint moduleId, out GSharpErrorSite site)
    {
        if (siteId == 0u || (siteId & BuiltinSiteFlag) != 0u)
        {
            site = default;
            return false;
        }

        if (moduleId != 0u && s_moduleSites.TryGetValue(moduleId, out var table))
        {
            if (table.TryGetValue(siteId, out site))
            {
                return true;
            }
        }

        // Fallback when moduleId == 0 or not found in module table
        foreach (var pair in s_moduleSites)
        {
            if (pair.Value.TryGetValue(siteId, out site))
            {
                return true;
            }
        }

        site = default;
        return false;
    }

    /// <summary>
    /// Packs 3D thread coordinates into a 32-bit unsigned integer (10 bits per dimension).
    /// </summary>
    /// <param name="tx">The 0-based thread X coordinate.</param>
    /// <param name="ty">The 0-based thread Y coordinate.</param>
    /// <param name="tz">The 0-based thread Z coordinate.</param>
    /// <returns>The packed 32-bit thread index.</returns>
    public static uint PackThreadIndex(uint tx, uint ty, uint tz) =>
        (tx & 0x3FFu) | ((ty & 0x3FFu) << 10) | ((tz & 0x3FFu) << 20);

    /// <summary>
    /// Unpacks 3D thread coordinates from a 32-bit packed value.
    /// </summary>
    /// <param name="threadLinear">The packed 32-bit thread coordinate value.</param>
    /// <returns>The unpacked <c>(X, Y, Z)</c> thread coordinates.</returns>
    public static (int X, int Y, int Z) UnpackThreadIndex(uint threadLinear) =>
        ((int)(threadLinear & 0x3FFu), (int)((threadLinear >> 10) & 0x3FFu), (int)((threadLinear >> 20) & 0x3FFu));

    /// <summary>
    /// Unpacks 3D block coordinates from the error slot fields <paramref name="blockX"/> and <paramref name="blockYZ"/> (#1056 item 2).
    /// </summary>
    /// <param name="blockX">The 32-bit block X coordinate (<c>blockIdx.x</c>).</param>
    /// <param name="blockYZ">The packed block Y and Z coordinates (<c>blockIdx.y | (blockIdx.z &lt;&lt; 16)</c>).</param>
    /// <returns>The unpacked <c>(X, Y, Z)</c> block coordinates.</returns>
    public static (int X, int Y, int Z) UnpackBlockIndex(uint blockX, uint blockYZ) =>
        ((int)blockX, (int)(blockYZ & 0xFFFFu), (int)((blockYZ >> 16) & 0xFFFFu));

    /// <summary>
    /// Unpacks 3D block coordinates from a linearized block index and grid dimensions.
    /// </summary>
    /// <param name="blockLinear">The linearized block index.</param>
    /// <param name="gridDim">The 3D grid dimensions <c>(X, Y, Z)</c>.</param>
    /// <returns>The unpacked <c>(X, Y, Z)</c> block coordinates.</returns>
    public static (int X, int Y, int Z) UnpackBlockIndex(uint blockLinear, (int X, int Y, int Z) gridDim)
    {
        var gx = gridDim.X > 0 ? (uint)gridDim.X : 1u;
        var gy = gridDim.Y > 0 ? (uint)gridDim.Y : 1u;
        var bx = (int)(blockLinear % gx);
        var q = blockLinear / gx;
        var by = (int)(q % gy);
        var bz = (int)(q / gy);
        return (bx, by, bz);
    }

    /// <summary>
    /// Unpacks the optional compact grid dimensions encoded in bits 20..31 of <see cref="GSharpDeviceErrorSlot.Code"/>.
    /// </summary>
    /// <param name="rawCode">The raw 32-bit code value from the device error slot.</param>
    /// <param name="fallbackGridDim">The fallback grid dimensions when compact dimensions were not encoded.</param>
    /// <returns>The resolved <c>(X, Y, Z)</c> grid dimensions.</returns>
    public static (int X, int Y, int Z) UnpackGridDimFromCode(uint rawCode, (int X, int Y, int Z) fallbackGridDim)
    {
        var gx = (int)((rawCode >> 20) & 0x3Fu);
        var gy = (int)((rawCode >> 26) & 0x3Fu);
        if (gx > 0 && gy > 0)
        {
            return (gx, gy, fallbackGridDim.Z > 0 ? fallbackGridDim.Z : 1);
        }

        return fallbackGridDim;
    }

    /// <summary>
    /// Returns the default base message for a <see cref="GSharpErrorCode"/> when neither a site message nor an explicit message is present.
    /// </summary>
    /// <param name="code">The runtime error code.</param>
    /// <returns>The default base message string.</returns>
    public static string GetDefaultMessage(GSharpErrorCode code) =>
        code switch
        {
            GSharpErrorCode.NotSupported => "Specified method is not supported.",
            GSharpErrorCode.InvalidOperation => "Operation is not valid due to the current state of the object.",
            GSharpErrorCode.IndexOutOfRange => "Index was outside the bounds of the array.",
            GSharpErrorCode.ArgumentOutOfRange => "Specified argument was out of the range of valid values.",
            GSharpErrorCode.Argument => "Value does not fall within the expected range.",
            GSharpErrorCode.NullReference => "Object reference not set to an instance of an object.",
            GSharpErrorCode.DivideByZero => "Attempted to divide by zero.",
            GSharpErrorCode.Overflow => "Arithmetic operation resulted in an overflow.",
            GSharpErrorCode.DataRace => "Shared memory data race detected.",
            _ => "A G# runtime error occurred."
        };

    /// <summary>
    /// Creates a standard .NET exception from a <see cref="GSharpDeviceErrorSlot"/>, populating <see cref="Exception.Data"/>.
    /// </summary>
    /// <param name="slot">The device error slot read from pinned host memory.</param>
    /// <param name="gridDim">Optional fallback 3D grid dimensions.</param>
    /// <param name="innerException">An optional inner exception (such as <see cref="CudaException"/>).</param>
    /// <param name="explicitMessage">An optional explicit message overriding the site message.</param>
    /// <param name="preserveRawMessageWhenNoSite">Whether to preserve <paramref name="explicitMessage"/> verbatim when no site metadata is registered.</param>
    /// <returns>The constructed standard .NET exception.</returns>
    public static Exception CreateExceptionFromSlot(
        in GSharpDeviceErrorSlot slot,
        (int X, int Y, int Z) gridDim = default,
        Exception? innerException = null,
        string? explicitMessage = null,
        bool preserveRawMessageWhenNoSite = false)
    {
        var rawCode = slot.Code;
        var code = (GSharpErrorCode)(rawCode & ErrorCodeMask);
        var (bx, by, bz) = UnpackBlockIndex(slot.BlockX, slot.BlockYZ);
        return CreateExceptionCore(
            code,
            slot.SiteId,
            slot.ModuleId,
            slot.ThreadLinear,
            (bx, by, bz),
            innerException,
            explicitMessage,
            preserveRawMessageWhenNoSite);
    }



    /// <summary>
    /// Creates a standard .NET exception for the specified <see cref="GSharpErrorCode"/>, explicit site ID, module ID, and block coordinates (#1056 item 2, item 5).
    /// </summary>
    /// <param name="code">The runtime error code.</param>
    /// <param name="siteId">The 1-based local error site ID.</param>
    /// <param name="moduleId">The 1-based module ID associated with the site table.</param>
    /// <param name="threadLinear">The packed thread coordinates.</param>
    /// <param name="blockCoord">The unpacked 3D block coordinates.</param>
    /// <param name="innerException">An optional inner exception.</param>
    /// <param name="explicitMessage">An optional explicit message.</param>
    /// <param name="preserveRawMessageWhenNoSite">Whether to preserve explicit message verbatim.</param>
    /// <returns>The constructed standard .NET exception.</returns>
    public static Exception CreateException(
        GSharpErrorCode code,
        uint siteId,
        uint moduleId,
        uint threadLinear,
        (int X, int Y, int Z) blockCoord,
        Exception? innerException = null,
        string? explicitMessage = null,
        bool preserveRawMessageWhenNoSite = false) =>
        CreateExceptionCore(
            code,
            siteId,
            moduleId,
            threadLinear,
            blockCoord,
            innerException,
            explicitMessage,
            preserveRawMessageWhenNoSite);

    private static Exception CreateExceptionCore(
        GSharpErrorCode code,
        uint siteId,
        uint moduleId,
        uint threadLinear,
        (int X, int Y, int Z) blockCoord,
        Exception? innerException,
        string? explicitMessage,
        bool preserveRawMessageWhenNoSite)
    {
        var hasSite = TryGetSite(siteId, moduleId, out var site);
        string baseMessage;
        if (!string.IsNullOrEmpty(explicitMessage))
        {
            baseMessage = explicitMessage;
        }
        else if (hasSite && !string.IsNullOrEmpty(site.DefaultMessage))
        {
            baseMessage = site.DefaultMessage;
        }
        else if ((siteId & BuiltinSiteFlag) != 0u && s_builtinMessages.TryGetValue(siteId, out var builtinMsg))
        {
            baseMessage = builtinMsg;
        }
        else
        {
            baseMessage = GetDefaultMessage(code);
        }

        var (tx, ty, tz) = UnpackThreadIndex(threadLinear);
        var threadCoord = $"({tx},{ty},{tz})";
        var blockCoordStr = $"({blockCoord.X},{blockCoord.Y},{blockCoord.Z})";

        string formattedMessage;
        if (hasSite)
        {
            var location = $"{site.File}({site.Line},{site.Column})";
            formattedMessage =
                $"{baseMessage} in {site.MemberName} at {location} in thread {threadCoord} of block {blockCoordStr}";
        }
        else if (preserveRawMessageWhenNoSite && !string.IsNullOrEmpty(explicitMessage))
        {
            formattedMessage = explicitMessage;
        }
        else
        {
            // Consistent exception message format across CUDA and Native CPU (#1056 item 1)
            formattedMessage = $"{baseMessage} in thread {threadCoord} of block {blockCoordStr}";
        }

        var exception = InstantiateException(code, formattedMessage, innerException);
        exception.Data["GSharp.ErrorCode"] = code.ToString();
        exception.Data["GSharp.ThreadIndex"] = threadCoord;
        exception.Data["GSharp.BlockIndex"] = blockCoordStr;

        if (hasSite)
        {
            exception.Data["GSharp.MemberName"] = site.MemberName;
            exception.Data["GSharp.SourceLocation"] = $"{site.File}({site.Line},{site.Column})";
        }

        if (innerException is CudaException cudaEx)
        {
            exception.Data["GSharp.CudaError"] = cudaEx.Result.ToString();
        }

        return exception;
    }

    private static Exception InstantiateException(GSharpErrorCode code, string message, Exception? innerException) =>
        code switch
        {
            GSharpErrorCode.NotSupported => new NotSupportedException(message, innerException),
            GSharpErrorCode.InvalidOperation => new InvalidOperationException(message, innerException),
            GSharpErrorCode.IndexOutOfRange => new IndexOutOfRangeException(message, innerException),
            GSharpErrorCode.ArgumentOutOfRange => new ArgumentOutOfRangeException(message, innerException),
            GSharpErrorCode.Argument => new ArgumentException(message, innerException),
            GSharpErrorCode.NullReference => new NullReferenceException(message, innerException),
            GSharpErrorCode.DivideByZero => new DivideByZeroException(message, innerException),
            GSharpErrorCode.Overflow => new OverflowException(message, innerException),
            GSharpErrorCode.DataRace => new InvalidOperationException(message, innerException),
            _ => new InvalidOperationException(message, innerException)
        };
}
