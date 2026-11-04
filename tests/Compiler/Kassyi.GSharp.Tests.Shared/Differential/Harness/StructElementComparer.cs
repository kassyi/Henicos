#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Custom element comparer used by <see cref="DiffBuffer{T}"/> to compare struct elements field-by-field
/// (ignoring undefined C#/C++ alignment padding bytes) or deep-struct array fields without reflection.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public interface IElementComparer<T>
{
    /// <summary>Returns whether <paramref name="expected"/> and <paramref name="actual"/> match under <paramref name="policy"/>.</summary>
    bool Equals(in T expected, in T actual, OutputPolicy policy);

    /// <summary>Formats <paramref name="value"/> for diagnostic failure messages.</summary>
    string Format(in T value);
}

/// <summary>
/// Context passed to <see cref="StructElementComparer{T}"/> field-check callbacks for comparing individual fields
/// or nested arrays of a struct without reflection.
/// </summary>
public sealed class StructFieldCheckContext
{
    private readonly OutputPolicy _defaultPolicy;
    private readonly List<string> _formattedFields = [];

    internal StructFieldCheckContext(OutputPolicy defaultPolicy, bool formatMode = false)
    {
        _defaultPolicy = defaultPolicy;
        FormatMode = formatMode;
    }

    /// <summary>Gets a value indicating whether the context is collecting field formatting rather than comparing.</summary>
    public bool FormatMode { get; }

    /// <summary>Gets a value indicating whether all checked fields matched.</summary>
    public bool IsMatch { get; private set; } = true;

    /// <summary>Gets the first mismatch description, if any.</summary>
    public string? FirstMismatchMessage { get; private set; }

    /// <summary>Records a custom failure for the struct comparison.</summary>
    public void Fail(string message)
    {
        if (IsMatch)
        {
            IsMatch = false;
            FirstMismatchMessage = message;
        }
    }

    /// <summary>
    /// Compares a scalar field under <paramref name="policy"/> (or the buffer's default policy).
    /// Unmanaged values without a custom comparer are compared as raw bytes; for a struct-typed field with padding,
    /// pass an <see cref="OutputPolicy"/> carrying its own <see cref="OutputPolicy.CustomComparer"/> (such as <see cref="OutputPolicy.ForStruct{T}(Action{StructFieldCheckContext, T, T}, OutputPolicy?)"/>).
    /// </summary>
    public void Field<TField>(string fieldName, TField expected, TField actual, OutputPolicy? policy = null)
    {
        if (FormatMode)
        {
            _formattedFields.Add($"{fieldName}={FormatValue(expected)}");
            return;
        }

        if (!IsMatch)
        {
            return;
        }

        var effectivePolicy = policy ?? _defaultPolicy;
        if (!ValuesMatch(in expected, in actual, effectivePolicy))
        {
            Fail($"{fieldName}: expected {FormatValue(expected)}, got {FormatValue(actual)}");
        }
    }

    /// <summary>Compares a field using <see cref="EqualityComparer{T}.Default"/>.</summary>
    public void Equal<TField>(string fieldName, TField expected, TField actual)
    {
        if (FormatMode)
        {
            _formattedFields.Add($"{fieldName}={FormatValue(expected)}");
            return;
        }

        if (!IsMatch)
        {
            return;
        }

        if (!EqualityComparer<TField>.Default.Equals(expected, actual))
        {
            Fail($"{fieldName}: expected {FormatValue(expected)}, got {FormatValue(actual)}");
        }
    }

    /// <summary>Compares an array field element-by-element under <paramref name="policy"/>.</summary>
    public void Array<TElement>(string fieldName, TElement[]? expected, TElement[]? actual, OutputPolicy? policy = null)
    {
        if (FormatMode)
        {
            _formattedFields.Add(expected is null ? $"{fieldName}=null" : $"{fieldName}=[{string.Join(", ", expected)}]");
            return;
        }

        if (!IsMatch)
        {
            return;
        }

        if (ReferenceEquals(expected, actual))
        {
            return;
        }

        if (expected is null || actual is null)
        {
            Fail($"{fieldName}: expected {(expected is null ? "null" : $"len={expected.Length}")}, got {(actual is null ? "null" : $"len={actual.Length}")}");
            return;
        }

        if (expected.Length != actual.Length)
        {
            Fail($"{fieldName}.Length: expected {expected.Length}, got {actual.Length}");
            return;
        }

        var effectivePolicy = policy ?? _defaultPolicy;
        for (int i = 0; i < expected.Length; i++)
        {
            if (!ValuesMatch(in expected[i], in actual[i], effectivePolicy))
            {
                Fail($"{fieldName}[{i}]: expected {FormatValue(expected[i])}, got {FormatValue(actual[i])}");
                return;
            }
        }
    }

    internal string BuildFormattedString(string fallback) =>
        _formattedFields.Count > 0 ? $"{{ {string.Join(", ", _formattedFields)} }}" : fallback;

    private static bool ValuesMatch<TField>(in TField expected, in TField actual, OutputPolicy policy)
    {
        if (policy.Kind == OutputPolicyKind.Ignore)
        {
            return true;
        }

        if (policy.CustomComparer is IElementComparer<TField> custom)
        {
            return custom.Equals(in expected, in actual, policy);
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<TField>())
        {
            int size = Unsafe.SizeOf<TField>();
            var expectedBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<TField, byte>(ref Unsafe.AsRef(in expected)), size);
            var actualBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<TField, byte>(ref Unsafe.AsRef(in actual)), size);
            if (expectedBytes.SequenceEqual(actualBytes))
            {
                return true;
            }

            if (policy.Kind == OutputPolicyKind.Bitwise)
            {
                return IsNaN(in expected) && IsNaN(in actual);
            }

            if (policy.Kind == OutputPolicyKind.Ulp)
            {
                return DiffBuffer<TField>.UlpEqualValue(in expected, in actual, policy.MaxUlp, policy.AllowInfinityBoundary);
            }

            return false;
        }

        return EqualityComparer<TField>.Default.Equals(expected, actual);
    }

    private static bool IsNaN<TField>(in TField value) => value switch
    {
        float f => float.IsNaN(f),
        double d => double.IsNaN(d),
        Half h => Half.IsNaN(h),
        _ => false,
    };

    private static string FormatValue<TField>(TField value) =>
        string.Format(CultureInfo.InvariantCulture, "{0}", value);
}

/// <summary>
/// Compares struct elements field-by-field using a compile-time delegate, avoiding reflection and ignoring struct padding bytes.
/// </summary>
/// <typeparam name="T">The struct type.</typeparam>
public sealed class StructElementComparer<T> : IElementComparer<T>
    where T : struct
{
    private readonly Func<T, T, OutputPolicy, bool> _fieldPredicate;
    private readonly Func<T, string> _formatter;

    /// <summary>Initializes a new instance of the <see cref="StructElementComparer{T}"/> class using a policy-aware predicate.</summary>
    public StructElementComparer(Func<T, T, OutputPolicy, bool> fieldPredicate, Func<T, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(fieldPredicate);
        _fieldPredicate = fieldPredicate;
        _formatter = formatter ?? (static v => v.ToString() ?? typeof(T).Name);
    }

    /// <summary>Initializes a new instance of the <see cref="StructElementComparer{T}"/> class using a boolean predicate.</summary>
    public StructElementComparer(Func<T, T, bool> fieldPredicate, Func<T, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(fieldPredicate);
        _fieldPredicate = (expected, actual, _) => fieldPredicate(expected, actual);
        _formatter = formatter ?? (static v => v.ToString() ?? typeof(T).Name);
    }

    /// <summary>Initializes a new instance of the <see cref="StructElementComparer{T}"/> class using a <see cref="StructFieldCheckContext"/> callback.</summary>
    public StructElementComparer(Action<StructFieldCheckContext, T, T> compareFields, Func<T, string>? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(compareFields);
        _fieldPredicate = (expected, actual, policy) =>
        {
            var ctx = new StructFieldCheckContext(policy);
            compareFields(ctx, expected, actual);
            return ctx.IsMatch;
        };
        _formatter = formatter ?? (v =>
        {
            var ctx = new StructFieldCheckContext(OutputPolicy.Exact, formatMode: true);
            compareFields(ctx, v, v);
            return ctx.BuildFormattedString(v.ToString() ?? typeof(T).Name);
        });
    }

    /// <inheritdoc/>
    public bool Equals(in T expected, in T actual, OutputPolicy policy) => _fieldPredicate(expected, actual, policy);

    /// <inheritdoc/>
    public string Format(in T value) => _formatter(value);
}

/// <summary>
/// Per-path array cloning context shared across top-level <see cref="DiffBuffers"/> array slots and
/// deep-struct slots so that reference aliasing (<c>ReferenceEquals</c>) is preserved both within struct fields
/// and between struct fields and top-level array arguments, without using reflection.
/// </summary>
public sealed class DiffArrayCloneScope
{
    private readonly Dictionary<object, object> _objectMap;

    /// <summary>Initializes a new standalone <see cref="DiffArrayCloneScope"/>.</summary>
    public DiffArrayCloneScope()
        : this(new Dictionary<object, object>(ReferenceEqualityComparer.Instance))
    {
    }

    internal DiffArrayCloneScope(Dictionary<object, object> objectMap) => _objectMap = objectMap;

    /// <summary>Clones <paramref name="source"/> once per scope, returning the same cloned instance if <paramref name="source"/> was already cloned.</summary>
    public TArray[]? CloneArray<TArray>(TArray[]? source)
    {
        if (source is null)
        {
            return null;
        }

        if (_objectMap.TryGetValue(source, out var existing))
        {
            return (TArray[])existing;
        }

        var copy = (TArray[])source.Clone();
        _objectMap.Add(source, copy);
        return copy;
    }
}
