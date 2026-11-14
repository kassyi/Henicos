namespace Kassyi.GSharp.Runtime.Aot;

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
        // A dynamic shared memory declaration of a negative count (__gsharp_dsmem_place of the CUDA prelude)
        dict[ComputeBuiltinMessageId("G#: the count of a shared memory declaration is negative")] =
            "G#: the count of a shared memory declaration is negative";

        // An ArraySegment<T> or ImmutableArray<T> without an array, and a null CopyTo destination
        // (__gsharp_view_message of the CUDA prelude); a null array passed to Array.Fill, Clear, Reverse or Copy
        // (__gsharp_array_argument, __gsharp_copy_arguments); Span.CopyTo into a shorter span
        foreach (var message in (ReadOnlySpan<string>)
                 [
                     "The underlying array is null.",
                     "Object reference not set to an instance of an object.",
                     "Value cannot be null. (Parameter 'destinationArray')",
                     "Value cannot be null. (Parameter 'sourceArray')",
                     "Value cannot be null. (Parameter 'array')",
                     "Destination is too short. (Parameter 'destination')",
                     // The argument errors of Array.Fill, Array.Copy and ElementAt in a device function, which has no
                     // error site table (ArgumentExceptionMessages of the compiler repeats these texts)
                     "Index was out of range. Must be non-negative and less than or equal to the size of the " +
                     "collection. " +
                     "(Parameter 'startIndex')",
                     "Count must be positive and count must refer to a location within the " +
                     "string/array/collection. " +
                     "(Parameter 'count')",
                     "Source array was not long enough. Check the source index, length, and the array's lower " +
                     "bounds. " +
                     "(Parameter 'sourceArray')",
                     "Destination array was not long enough. Check the destination index, length, and the array's " +
                     "lower bounds. " +
                     "(Parameter 'destinationArray')",
                     "length must be a non-negative value. (Parameter 'length')",
                     "sourceIndex must be greater than or equal to '0'. (Parameter 'sourceIndex')",
                     "destinationIndex must be greater than or equal to '0'. (Parameter 'destinationIndex')",
                     "Index was out of range. Must be non-negative and less than the size of the collection. " +
                     "(Parameter 'index')",
                 ])
        {
            dict[ComputeBuiltinMessageId(message)] = message;
        }

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
    /// Unpacks 3D block coordinates from the error slot fields <paramref name="blockX"/> and <paramref name="blockYZ"/>.
    /// </summary>
    /// <param name="blockX">The 32-bit block X coordinate (<c>blockIdx.x</c>).</param>
    /// <param name="blockYZ">The packed block Y and Z coordinates (<c>blockIdx.y | (blockIdx.z &lt;&lt; 16)</c>).</param>
    /// <returns>The unpacked <c>(X, Y, Z)</c> block coordinates.</returns>
    public static (int X, int Y, int Z) UnpackBlockIndex(uint blockX, uint blockYZ) =>
        ((int)blockX, (int)(blockYZ & 0xFFFFu), (int)((blockYZ >> 16) & 0xFFFFu));

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
            GSharpErrorCode.SwitchExpression => "Non-exhaustive switch expression failed to match its input.",
            GSharpErrorCode.ArgumentNull => "Value cannot be null.",
            _ => "A G# runtime error occurred."
        };

    /// <summary>
    /// Creates a standard .NET exception from a <see cref="GSharpDeviceErrorSlot"/>, populating <see cref="Exception.Data"/>.
    /// </summary>
    /// <param name="slot">The device error slot read from pinned host memory.</param>
    /// <param name="innerException">An optional inner exception (such as <see cref="CudaException"/>).</param>
    /// <param name="explicitMessage">An optional explicit message overriding the site message.</param>
    /// <param name="preserveRawMessageWhenNoSite">Whether to preserve <paramref name="explicitMessage"/> verbatim when no site metadata is registered.</param>
    /// <returns>The constructed standard .NET exception.</returns>
    public static Exception CreateExceptionFromSlot(
        in GSharpDeviceErrorSlot slot,
        Exception? innerException = null,
        string? explicitMessage = null,
        bool preserveRawMessageWhenNoSite = false)
    {
        var rawCode = slot.Code;
        var code = (GSharpErrorCode)(rawCode & ErrorCodeMask);
        var (bx, by, bz) = UnpackBlockIndex(slot.BlockX, slot.BlockYZ);
        return CreateExceptionCore(
            code,
            new ErrorSiteLocation(slot.SiteId, slot.ModuleId, slot.ThreadLinear, (bx, by, bz)),
            innerException,
            explicitMessage,
            preserveRawMessageWhenNoSite);
    }

    /// <summary>
    /// Creates a standard .NET exception for the specified <see cref="GSharpErrorCode"/>, explicit site ID, module ID, and block coordinates.
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
            new ErrorSiteLocation(siteId, moduleId, threadLinear, blockCoord),
            innerException,
            explicitMessage,
            preserveRawMessageWhenNoSite);

    private static Exception CreateExceptionCore(
        GSharpErrorCode code,
        in ErrorSiteLocation location,
        Exception? innerException,
        string? explicitMessage,
        bool preserveRawMessageWhenNoSite)
    {
        GSharpErrorSite? site = TryGetSite(location.SiteId, location.ModuleId, out var found) ? found : null;
        var baseMessage = ResolveBaseMessage(code, location.SiteId, explicitMessage, site);
        // ArgumentOutOfRangeException and ArgumentNullException have no constructor with both a parameter name and an
        // inner exception: with one, the name stays in the text only
        var paramName = innerException is null || code == GSharpErrorCode.Argument
            ? SplitParameterName(code, ref baseMessage)
            : null;
        var (tx, ty, tz) = UnpackThreadIndex(location.ThreadLinear);
        var threadCoord = $"({tx},{ty},{tz})";
        var blockCoord = $"({location.BlockCoord.X},{location.BlockCoord.Y},{location.BlockCoord.Z})";
        // The explicit message as it is, except that the name taken from its end is no longer part of the text
        var rawMessage = !preserveRawMessageWhenNoSite ? null : paramName is null ? explicitMessage : baseMessage;
        var message = FormatMessage(baseMessage, site, threadCoord, blockCoord, rawMessage);

        var exception = InstantiateException(code, message, paramName, innerException);
        PopulateExceptionData(exception, code, threadCoord, blockCoord, site);
        return exception;
    }

    /// <summary>
    /// The explicit message, else the message of the site, else the builtin message of the prelude, else the default.
    /// </summary>
    private static string ResolveBaseMessage(
        GSharpErrorCode code,
        uint siteId,
        string? explicitMessage,
        GSharpErrorSite? site)
    {
        if (!string.IsNullOrEmpty(explicitMessage))
        {
            return explicitMessage;
        }

        if (site is { DefaultMessage: { Length: > 0 } siteMessage })
        {
            return siteMessage;
        }

        if ((siteId & BuiltinSiteFlag) != 0u && s_builtinMessages.TryGetValue(siteId, out var builtinMessage))
        {
            return builtinMessage;
        }

        return GetDefaultMessage(code);
    }

    // rawMessage: the explicit message when the caller keeps it verbatim for an error without a site, else null.
    private static string FormatMessage(
        string baseMessage,
        GSharpErrorSite? site,
        string threadCoord,
        string blockCoord,
        string? rawMessage)
    {
        if (site is { } knownSite)
        {
            var sourceLocation = $"{knownSite.File}({knownSite.Line},{knownSite.Column})";
            return $"{baseMessage} in {knownSite.MemberName} at {sourceLocation} " +
                $"in thread {threadCoord} of block {blockCoord}";
        }

        if (!string.IsNullOrEmpty(rawMessage))
        {
            return rawMessage;
        }

        // Consistent exception message format across CUDA and Native CPU
        return $"{baseMessage} in thread {threadCoord} of block {blockCoord}";
    }

    private static void PopulateExceptionData(
        Exception exception,
        GSharpErrorCode code,
        string threadCoord,
        string blockCoord,
        GSharpErrorSite? site)
    {
        exception.Data["GSharp.ErrorCode"] = code.ToString();
        exception.Data["GSharp.ThreadIndex"] = threadCoord;
        exception.Data["GSharp.BlockIndex"] = blockCoord;

        if (site is { } knownSite)
        {
            exception.Data["GSharp.MemberName"] = knownSite.MemberName;
            exception.Data["GSharp.SourceLocation"] = $"{knownSite.File}({knownSite.Line},{knownSite.Column})";
        }

        if (exception.InnerException is CudaException cudaEx)
        {
            exception.Data["GSharp.CudaError"] = cudaEx.Result.ToString();
        }
    }

    // The message of an argument error ends with the name of the parameter as .NET words it, "(Parameter 'x')": the
    // check writes it into its message, so that it reaches the exception through every path (the error record has no
    // field for it). The exception takes the name as ParamName, and its Message appends the same words again
    private static string? SplitParameterName(GSharpErrorCode code, ref string message)
    {
        if (code is not (GSharpErrorCode.Argument or GSharpErrorCode.ArgumentNull or GSharpErrorCode.ArgumentOutOfRange)
            || !message.EndsWith("')", StringComparison.Ordinal))
        {
            return null;
        }

        const string Marker = " (Parameter '";
        var start = message.LastIndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var nameStart = start + Marker.Length;
        var name = message[nameStart..^2];
        if (name.Length == 0)
        {
            // new ArgumentException(message, "") drops the suffix: the text stays as it is
            return null;
        }

        message = message[..start];
        return name;
    }

    private static Exception InstantiateException(
        GSharpErrorCode code,
        string message,
        string? paramName,
        Exception? innerException) =>
        code switch
        {
            GSharpErrorCode.NotSupported => new NotSupportedException(message, innerException),
            GSharpErrorCode.InvalidOperation => new InvalidOperationException(message, innerException),
            GSharpErrorCode.IndexOutOfRange => new IndexOutOfRangeException(message, innerException),
            GSharpErrorCode.ArgumentOutOfRange => paramName is null
                ? new ArgumentOutOfRangeException(message, innerException)
                : new ArgumentOutOfRangeException(paramName, message),
            GSharpErrorCode.Argument => paramName is null
                ? new ArgumentException(message, innerException)
                : new ArgumentException(message, paramName, innerException),
            GSharpErrorCode.NullReference => new NullReferenceException(message, innerException),
            GSharpErrorCode.DivideByZero => new DivideByZeroException(message, innerException),
            GSharpErrorCode.Overflow => new OverflowException(message, innerException),
            GSharpErrorCode.DataRace => new InvalidOperationException(message, innerException),
            GSharpErrorCode.SwitchExpression =>
                new System.Runtime.CompilerServices.SwitchExpressionException(message, innerException),
            GSharpErrorCode.ArgumentNull => paramName is null
                ? new ArgumentNullException(message, innerException)
                : new ArgumentNullException(paramName, message),
            _ => new InvalidOperationException(message, innerException)
        };
}
