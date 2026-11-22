using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MicroJson.SourceGenerator;

/// <summary>
/// Generates serialization code for classes annotated with <c>[MicroJsonSerializable]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MicroJsonGenerator : IIncrementalGenerator
{
    internal const string SerializableAttributeName = "MicroJson.MicroJsonSerializableAttribute";

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            SerializableAttributeName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => ContextGenerator.Generate(ctx, ct));

        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());

            if (result.HintName != null && result.Source != null)
                spc.AddSource(result.HintName, result.Source);
        });
    }
}

/// <summary>
/// The output of the generator for one context class. Contains only equatable data so that the incremental pipeline can cache it.
/// </summary>
sealed record GenerationResult(string? HintName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics)
{
    public static readonly GenerationResult Empty = new(null, null, EquatableArray<DiagnosticInfo>.Empty);
}

sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location? location)
    {
        if (location?.SourceTree is null) return null;
        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.ToArray<object>());
}

/// <summary>
/// An immutable array with value equality.
/// </summary>
readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new([]);

    readonly T[]? items = items;

    T[] Items => items ?? [];

    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var item in Items)
            hash = (hash * 31) + item.GetHashCode();
        return hash;
    }

    public TResult[] ToArray<TResult>() => Items.Cast<TResult>().ToArray();

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

static class Diagnostics
{
    const string Category = "MicroJson";

    public static readonly DiagnosticDescriptor NotPartial = new(
        "MJ0001", "Class must be partial",
        "The class '{0}' and all its containing types must be declared partial to generate MicroJson serialization code",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "MJ0002", "Type is not supported",
        "The type '{0}' cannot be serialized: {1}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMember = new(
        "MJ0003", "Member is not supported",
        "The member '{0}' is ignored because its type '{1}' cannot be serialized: {2}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotConstructible = new(
        "MJ0004", "Type cannot be deserialized",
        "The type '{0}' cannot be deserialized: {1}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericContext = new(
        "MJ0005", "Class must not be generic",
        "The class '{0}' must not be generic to generate MicroJson serialization code",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateName = new(
        "MJ0006", "Duplicate JSON property name",
        "The member '{0}' is ignored because another member of '{1}' already uses the JSON name '{2}'",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
