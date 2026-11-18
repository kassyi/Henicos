using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// A plan describing the warning and nullable directives to restore after an emitted managed method.
/// </summary>
internal readonly record struct ManagedWarningRestorePlan(
    bool HasDisableAll,
    EquatableArray<string> WarningCodes,
    bool HasNullableDirective)
{
    public bool IsEmpty => !HasDisableAll && WarningCodes.IsDefaultOrEmpty && !HasNullableDirective;
}

/// <summary>
/// Emits restore directives for any warnings disabled or nullable directives declared in a method body,
/// ensuring that suppressions do not leak across generated members in the same file.
/// </summary>
internal static class ManagedWarningRestore
{
    /// <summary>
    /// Creates a restore plan by scanning descendant directive trivia within the original method declaration.
    /// </summary>
    internal static ManagedWarningRestorePlan Create(MethodDeclarationSyntax declaration)
    {
        if (declaration is null)
        {
            return default;
        }

        SyntaxNode? bodyNode = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
        if (bodyNode is null)
        {
            return default;
        }

        var hasDisableAll = false;
        var hasNullableDirective = false;
        ImmutableArray<string>.Builder? warningCodes = null;
        HashSet<string>? seenCodes = null;

        foreach (var trivia in bodyNode.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!trivia.IsDirective)
            {
                continue;
            }

            var structure = trivia.GetStructure();
            if (structure is PragmaWarningDirectiveTriviaSyntax pragma)
            {
                if (pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
                {
                    if (pragma.ErrorCodes.Count == 0)
                    {
                        hasDisableAll = true;
                    }
                    else
                    {
                        foreach (var errorCode in pragma.ErrorCodes)
                        {
                            var code = errorCode.ToString().Trim();
                            if (code.Length > 0)
                            {
                                warningCodes ??= ImmutableArray.CreateBuilder<string>();
                                seenCodes ??= new HashSet<string>(StringComparer.Ordinal);
                                if (seenCodes.Add(code))
                                {
                                    warningCodes.Add(code);
                                }
                            }
                        }
                    }
                }
            }
            else if (structure is NullableDirectiveTriviaSyntax)
            {
                hasNullableDirective = true;
            }
        }

        var codes = warningCodes is { Count: > 0 }
            ? warningCodes.ToImmutable().AsEquatableArray()
            : default;

        return new ManagedWarningRestorePlan(hasDisableAll, codes, hasNullableDirective);
    }

    // #pragma warning restore <id1>, <id2>
    // #pragma warning restore
    // #nullable restore
    /// <summary>
    /// Appends warning and nullable restore directives according to the specified plan.
    /// </summary>
    internal static void Append(SourceWriter writer, in ManagedWarningRestorePlan plan)
    {
        if (plan.IsEmpty)
        {
            return;
        }

        // Ensure we start at column 0 on a new line
        writer.AppendLine();

        if (!plan.WarningCodes.IsDefaultOrEmpty)
        {
            writer.Append("#pragma warning restore ");
            for (var i = 0; i < plan.WarningCodes.Length; i++)
            {
                if (i > 0)
                {
                    writer.Append(", ");
                }
                writer.Append(plan.WarningCodes[i]);
            }
            writer.AppendLine();
        }

        if (plan.HasDisableAll)
        {
            writer.AppendLine("#pragma warning restore");
        }

        if (plan.HasNullableDirective)
        {
            writer.AppendLine("#nullable restore");
        }
    }
}
