#nullable enable
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Base discoverer that extracts strongly-typed enum traits from xUnit <see cref="IAttributeInfo"/>.
/// </summary>
public abstract class EnumTraitDiscoverer<TEnum>(string primaryKey, bool emitCategoryAlias) : ITraitDiscoverer
    where TEnum : struct, Enum
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        if (traitAttribute is null)
        {
            yield break;
        }

        object? firstArg = null;
        bool hasArg = false;
        foreach (var arg in traitAttribute.GetConstructorArguments())
        {
            firstArg = arg;
            hasArg = true;
            break;
        }

        if (!hasArg || firstArg is null)
        {
            yield break;
        }

        string? valueStr = firstArg switch
        {
            TEnum enumVal when Enum.IsDefined(typeof(TEnum), enumVal) => enumVal.ToString(),
            int intVal => Enum.GetName(typeof(TEnum), intVal),
            string strVal when Enum.TryParse<TEnum>(strVal, ignoreCase: false, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed) => parsed.ToString(),
            _ => null
        };

        if (string.IsNullOrEmpty(valueStr))
        {
            yield break;
        }

        yield return new KeyValuePair<string, string>(primaryKey, valueStr);
        if (emitCategoryAlias)
        {
            yield return new KeyValuePair<string, string>("Category", valueStr);
        }
    }
}

/// <summary>
/// Discovers test traits from <see cref="TestTargetAttribute"/>.
/// Emits both 'TestTarget' and 'Category' keys for seamless CLI filtering and backward compatibility.
/// </summary>
public sealed class TestTargetDiscoverer() : EnumTraitDiscoverer<ExecutionTarget>("TestTarget", emitCategoryAlias: true);

/// <summary>
/// Discovers test traits from <see cref="CategoryAttribute"/>.
/// Emits the 'Category' key.
/// </summary>
public sealed class CategoryDiscoverer() : EnumTraitDiscoverer<TestCategory>("Category", emitCategoryAlias: false);

/// <summary>
/// Discovers test traits from <see cref="ScenarioAttribute"/>.
/// Emits both 'Scenario' and 'Category' keys for seamless CLI filtering and backward compatibility.
/// </summary>
public sealed class ScenarioDiscoverer() : EnumTraitDiscoverer<TestScenario>("Scenario", emitCategoryAlias: true);
