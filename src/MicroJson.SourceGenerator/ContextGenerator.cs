using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MicroJson.SourceGenerator;

enum Kind
{
    String,
    Boolean,
    Char,
    Number,
    DateTime,
    DateTimeOffset,
    Parsable,
    Uri,
    Dom,
    Enum,
    Nullable,
    Array,
    List,
    Dictionary,
    Object,
}

enum WriteKind
{
    None,
    Set,
    Init,
}

sealed class TypeModel(ITypeSymbol symbol, string id, Kind kind)
{
    public ITypeSymbol Symbol { get; } = symbol;
    public string Id { get; } = id;
    public Kind Kind { get; } = kind;
    public string Name { get; } = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    /// <summary>The underlying type of a nullable, the element type of a collection, or the value type of a dictionary.</summary>
    public TypeModel? Element { get; init; }
    /// <summary>The format string for <see cref="Kind.Parsable"/> types.</summary>
    public string? Format { get; init; }
    public bool CanBeNull => Symbol.IsReferenceType || Kind == Kind.Nullable;
}

sealed class MemberModel(ISymbol symbol, string jsonName, TypeModel type, bool canRead, WriteKind write)
{
    public ISymbol Symbol { get; } = symbol;
    public string Name { get; } = ContextGenerator.Identifier(symbol.Name);
    public string JsonName { get; } = jsonName;
    public TypeModel Type { get; } = type;
    public bool CanRead { get; } = canRead;
    public WriteKind Write { get; } = write;
    public IParameterSymbol? Parameter { get; set; }
}

/// <summary>
/// Analyzes a context class and generates its serialization code.
/// </summary>
sealed class ContextGenerator
{
    const string OptionsAttributeName = "MicroJson.MicroJsonOptionsAttribute";
    const string PropertyNameAttributeName = "MicroJson.MicroJsonPropertyNameAttribute";
    const string IgnoreAttributeName = "MicroJson.MicroJsonIgnoreAttribute";
    const string Writer = "global::MicroJson.JsonWriter";
    const string Reader = "global::MicroJson.JsonReader";

    static readonly string[] ListTypes = ["List`1", "IList`1", "ICollection`1", "IEnumerable`1", "IReadOnlyList`1", "IReadOnlyCollection`1"];
    static readonly string[] DictionaryTypes = ["Dictionary`2", "IDictionary`2", "IReadOnlyDictionary`2"];

    readonly INamedTypeSymbol context;
    readonly Compilation compilation;
    readonly CancellationToken cancellationToken;
    readonly List<DiagnosticInfo> diagnostics = [];
    readonly Dictionary<ITypeSymbol, TypeModel> types = new(SymbolEqualityComparer.Default);
    readonly Dictionary<ITypeSymbol, string> failures = new(SymbolEqualityComparer.Default);
    readonly Queue<TypeModel> pending = new();
    readonly HashSet<string> ids = new(StringComparer.Ordinal);
    readonly StringBuilder code = new();

    int namingPolicy;
    int ignoreCondition;
    bool caseInsensitive;

    ContextGenerator(INamedTypeSymbol context, Compilation compilation, CancellationToken cancellationToken)
    {
        this.context = context;
        this.compilation = compilation;
        this.cancellationToken = cancellationToken;
    }

    public static GenerationResult Generate(GeneratorAttributeSyntaxContext ctx, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)ctx.TargetSymbol;

        // a partial class may carry attributes in several declarations; generate only once
        if (!IsFirstAnnotatedDeclaration(ctx, symbol, cancellationToken))
            return GenerationResult.Empty;

        return new ContextGenerator(symbol, ctx.SemanticModel.Compilation, cancellationToken).Run();
    }

    static bool IsFirstAnnotatedDeclaration(GeneratorAttributeSyntaxContext ctx, INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        var first = symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == MicroJsonGenerator.SerializableAttributeName)
            .Select(a => a.ApplicationSyntaxReference)
            .OfType<SyntaxReference>()
            .OrderBy(r => r.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(r => r.Span.Start)
            .FirstOrDefault();

        cancellationToken.ThrowIfCancellationRequested();

        return first == null || (first.SyntaxTree == ctx.TargetNode.SyntaxTree && ctx.TargetNode.Span.Contains(first.Span));
    }

    GenerationResult Run()
    {
        var location = context.Locations.FirstOrDefault();

        for (var t = context; t != null; t = t.ContainingType)
        {
            if (t.IsGenericType)
                return Fail(Diagnostics.GenericContext, location, context.ToDisplayString());

            if (!t.DeclaringSyntaxReferences.All(r => r.GetSyntax(cancellationToken) is TypeDeclarationSyntax d && d.Modifiers.Any(SyntaxKind.PartialKeyword)))
                return Fail(Diagnostics.NotPartial, location, context.ToDisplayString());
        }

        ReadOptions();

        var roots = new List<TypeModel>();

        foreach (var attribute in context.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == MicroJsonGenerator.SerializableAttributeName))
        {
            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not ITypeSymbol type)
                continue;

            var model = Resolve(type, out var reason);

            if (model == null)
            {
                if (reason != null)
                    Report(Diagnostics.UnsupportedType, attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(), type.ToDisplayString(), reason);
            }
            else if (!roots.Contains(model))
            {
                roots.Add(model);
            }
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmitType(pending.Dequeue());
        }

        var source = BuildSource(roots);
        var hintName = Sanitize(context.ToDisplayString()) + ".MicroJson.g.cs";

        return new GenerationResult(hintName, source, new EquatableArray<DiagnosticInfo>([.. diagnostics]));
    }

    GenerationResult Fail(DiagnosticDescriptor descriptor, Location? location, params string[] args)
    {
        Report(descriptor, location, args);
        return new GenerationResult(null, null, new EquatableArray<DiagnosticInfo>([.. diagnostics]));
    }

    void Report(DiagnosticDescriptor descriptor, Location? location, params string[] args)
    {
        diagnostics.Add(new DiagnosticInfo(descriptor, LocationInfo.From(location), new EquatableArray<string>(args)));
    }

    void ReadOptions()
    {
        var options = context.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == OptionsAttributeName);
        if (options == null) return;

        foreach (var arg in options.NamedArguments)
        {
            switch (arg.Key)
            {
                case "PropertyNamingPolicy":
                    namingPolicy = arg.Value.Value is int n ? n : 0;
                    break;
                case "DefaultIgnoreCondition":
                    ignoreCondition = arg.Value.Value is int i ? i : 0;
                    break;
                case "PropertyNameCaseInsensitive":
                    caseInsensitive = arg.Value.Value is true;
                    break;
            }
        }
    }

    TypeModel? Resolve(ITypeSymbol type, out string? reason)
    {
        reason = null;

        if (types.TryGetValue(type, out var existing))
            return existing;

        if (failures.TryGetValue(type, out reason))
            return null;

        var model = Classify(type, out reason);

        if (model == null && reason != null)
            failures[type] = reason;

        return model;
    }

    TypeModel? Classify(ITypeSymbol type, out string? reason)
    {
        reason = null;

        // errors are reported by the compiler
        if (type.TypeKind == TypeKind.Error)
            return null;

        if (!compilation.IsSymbolAccessibleWithin(type, context))
        {
            reason = "it is not accessible from '" + context.ToDisplayString() + "'";
            return null;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return Add(type, Kind.String);
            case SpecialType.System_Boolean:
                return Add(type, Kind.Boolean);
            case SpecialType.System_Char:
                return Add(type, Kind.Char);
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return Add(type, Kind.Number);
            case SpecialType.System_DateTime:
                return Add(type, Kind.DateTime);
            case SpecialType.System_Object:
                return Add(type, Kind.Dom);
        }

        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                reason = "multi-dimensional arrays are not supported";
                return null;
            }

            return AddWithElement(type, Kind.Array, array.ElementType, out reason);
        }

        if (type is not INamedTypeSymbol named || named.IsUnboundGenericType)
        {
            reason = "the type is not supported";
            return null;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return AddWithElement(type, Kind.Nullable, named.TypeArguments[0], out reason);

        if (named.TypeKind == TypeKind.Enum)
            return Add(type, Kind.Enum);

        var ns = named.ContainingType == null ? named.ContainingNamespace.ToDisplayString() : null;

        if (ns == "System")
        {
            switch (named.MetadataName)
            {
                case "DateTimeOffset":
                    return Add(type, Kind.DateTimeOffset);
                case "DateOnly":
                case "TimeOnly":
                    return Add(type, Kind.Parsable, "O");
                case "TimeSpan":
                    return Add(type, Kind.Parsable, "c");
                case "Guid":
                    return Add(type, Kind.Parsable, "D");
                case "Uri":
                    return Add(type, Kind.Uri);
            }
        }

        if (ns == "System.Collections.Generic")
        {
            if (ListTypes.Contains(named.MetadataName))
                return AddWithElement(type, Kind.List, named.TypeArguments[0], out reason);

            if (DictionaryTypes.Contains(named.MetadataName))
            {
                if (named.TypeArguments[0].SpecialType != SpecialType.System_String)
                {
                    reason = "dictionary keys must be strings";
                    return null;
                }

                return AddWithElement(type, Kind.Dictionary, named.TypeArguments[1], out reason);
            }
        }

        var containingNamespace = named.ContainingNamespace.ToDisplayString();

        if (containingNamespace == "System" || containingNamespace.StartsWith("System.", StringComparison.Ordinal))
        {
            reason = "the type is not supported";
            return null;
        }

        if (named.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
        {
            reason = "only arrays, List<T> and its interfaces, and Dictionary<string, TValue> and its interfaces are supported as collections";
            return null;
        }

        if (named.TypeKind is TypeKind.Class or TypeKind.Struct && !named.IsStatic && !named.IsRefLikeType)
            return Add(type, Kind.Object);

        reason = "the type is not supported";
        return null;
    }

    TypeModel? AddWithElement(ITypeSymbol type, Kind kind, ITypeSymbol elementType, out string? reason)
    {
        var element = Resolve(elementType, out var elementReason);

        if (element == null)
        {
            reason = elementReason == null ? null : "the type '" + elementType.ToDisplayString() + "' is not supported: " + elementReason;
            return null;
        }

        reason = null;

        var model = new TypeModel(type, MakeId(type), kind) { Element = element };
        types[type] = model;
        pending.Enqueue(model);

        return model;
    }

    TypeModel Add(ITypeSymbol type, Kind kind, string? format = null)
    {
        var model = new TypeModel(type, MakeId(type), kind) { Format = format };
        types[type] = model;
        pending.Enqueue(model);
        return model;
    }

    string MakeId(ITypeSymbol type)
    {
        var id = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? "Nullable_" + Sanitize(nullable.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            : Sanitize(type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        var unique = id;

        for (int i = 2; !ids.Add(unique); i++)
            unique = id + i;

        return unique;
    }

    static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);

        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');

        return sb.ToString().TrimEnd('_');
    }

    internal static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    static string Literal(string s) => SymbolDisplay.FormatLiteral(s, quote: true);

    void Line(string line = "")
    {
        if (line.Length > 0)
            code.Append("        ");
        code.AppendLine(line);
    }

    void EmitType(TypeModel model)
    {
        var members = model.Kind == Kind.Object ? AnalyzeMembers(model) : null;

        Line($"private static void Write_{model.Id}({Writer} w, {model.Name} value)");
        Line("{");
        EmitWrite(model, members);
        Line("}");
        Line();
        Line($"private static {model.Name} Read_{model.Id}({Reader} r)");
        Line("{");
        EmitRead(model, members);
        Line("}");
        Line();
    }

    void EmitWrite(TypeModel model, List<MemberModel>? members)
    {
        var element = model.Element;

        switch (model.Kind)
        {
            case Kind.String:
                Line("    w.WriteString(value);");
                break;
            case Kind.Boolean:
                Line("    w.WriteBoolean(value);");
                break;
            case Kind.Char:
                Line("    w.WriteString(value.ToString());");
                break;
            case Kind.Number:
                Line("    w.WriteNumber(value);");
                break;
            case Kind.DateTime:
            case Kind.DateTimeOffset:
                Line("    w.WriteFormatted(value, \"O\");");
                break;
            case Kind.Parsable:
                Line($"    w.WriteFormatted(value, {Literal(model.Format!)});");
                break;
            case Kind.Uri:
                Line("    w.WriteString(value?.OriginalString);");
                break;
            case Kind.Dom:
                Line("    w.WriteValue(value);");
                break;
            case Kind.Nullable:
                Line($"    if (value.HasValue) Write_{element!.Id}(w, value.GetValueOrDefault());");
                Line("    else w.WriteNull();");
                break;
            case Kind.Array:
            case Kind.List:
                Line("    if (value is null) { w.WriteNull(); return; }");
                Line("    w.WriteStartArray();");
                Line($"    foreach (var item in value) Write_{element!.Id}(w, item);");
                Line("    w.WriteEndArray();");
                break;
            case Kind.Dictionary:
                Line("    if (value is null) { w.WriteNull(); return; }");
                Line("    w.WriteStartObject();");
                Line("    foreach (var kvp in value)");
                Line("    {");
                Line("        w.WritePropertyName(kvp.Key);");
                Line($"        Write_{element!.Id}(w, kvp.Value);");
                Line("    }");
                Line("    w.WriteEndObject();");
                break;
            case Kind.Enum:
                EmitEnumWrite(model);
                break;
            case Kind.Object:
                EmitObjectWrite(model, members!);
                break;
        }
    }

    void EmitRead(TypeModel model, List<MemberModel>? members)
    {
        var element = model.Element;

        switch (model.Kind)
        {
            case Kind.String:
                Line("    return r.TryReadNull() ? null : r.ReadString();");
                break;
            case Kind.Boolean:
                Line("    return r.ReadBoolean();");
                break;
            case Kind.Char:
                Line("    var s = r.ReadString();");
                Line("    if (s.Length != 1) throw r.CreateException(\"expected a single character\");");
                Line("    return s[0];");
                break;
            case Kind.Number:
                Line($"    return r.ReadNumber<{model.Name}>();");
                break;
            case Kind.DateTime:
                Line("    return r.ReadDateTime();");
                break;
            case Kind.DateTimeOffset:
                Line("    return r.ReadDateTimeOffset();");
                break;
            case Kind.Parsable:
                Line($"    return r.ReadParsable<{model.Name}>();");
                break;
            case Kind.Uri:
                Line("    if (r.TryReadNull()) return null;");
                Line("    if (!global::System.Uri.TryCreate(r.ReadString(), global::System.UriKind.RelativeOrAbsolute, out var uri)) throw r.CreateException(\"invalid URI\");");
                Line("    return uri;");
                break;
            case Kind.Dom:
                Line("    return r.ReadValue();");
                break;
            case Kind.Nullable:
                Line("    if (r.TryReadNull()) return null;");
                Line($"    return Read_{element!.Id}(r);");
                break;
            case Kind.Array:
            case Kind.List:
                Line("    if (r.TryReadNull()) return null;");
                Line($"    var list = new global::System.Collections.Generic.List<{element!.Name}>();");
                Line("    r.ReadStartArray();");
                Line($"    while (r.TryReadNextItem()) list.Add(Read_{element.Id}(r));");
                Line(model.Kind == Kind.Array ? "    return list.ToArray();" : "    return list;");
                break;
            case Kind.Dictionary:
                Line("    if (r.TryReadNull()) return null;");
                Line($"    var dict = new global::System.Collections.Generic.Dictionary<string, {element!.Name}>();");
                Line("    r.ReadStartObject();");
                Line($"    while (r.TryReadPropertyName(out var name)) dict[name] = Read_{element.Id}(r);");
                Line("    return dict;");
                break;
            case Kind.Enum:
                EmitEnumRead(model);
                break;
            case Kind.Object:
                EmitObjectRead(model, members!);
                break;
        }
    }

    static List<(string Name, object Value)> EnumFields(TypeModel model) =>
        model.Symbol.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue && f.ConstantValue != null)
            .Select(f => (Identifier(f.Name), f.ConstantValue!))
            .ToList();

    static bool IsFlags(TypeModel model) =>
        model.Symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute");

    static string UnderlyingType(TypeModel model) =>
        ((INamedTypeSymbol)model.Symbol).EnumUnderlyingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    void EmitEnumWrite(TypeModel model)
    {
        var fields = EnumFields(model);
        var seenValues = new HashSet<object>();

        Line("    switch (value)");
        Line("    {");
        foreach (var (name, value) in fields)
        {
            // several names can share a value; the first one wins (a duplicate case label would not compile)
            if (seenValues.Add(value))
                Line($"        case {model.Name}.{name}: w.WriteString({Literal(name.TrimStart('@'))}); return;");
        }
        Line("    }");

        if (IsFlags(model))
        {
            Line("    string s = null;");
            Line("    var rest = value;");
            foreach (var (name, _) in fields.Where(f => Convert.ToDecimal(f.Value) != 0).OrderByDescending(f => Convert.ToDecimal(f.Value)))
            {
                var member = model.Name + "." + name;
                var json = name.TrimStart('@');
                Line($"    if ((rest & {member}) == {member}) {{ s = s == null ? {Literal(json)} : {Literal(json + ", ")} + s; rest &= ~{member}; }}");
            }
            Line("    if (rest == 0 && s != null) { w.WriteString(s); return; }");
        }

        Line($"    w.WriteNumber(({UnderlyingType(model)})value);");
    }

    void EmitEnumRead(TypeModel model)
    {
        var fields = EnumFields(model);

        Line($"    if (r.Peek() == global::MicroJson.JsonTokenType.Number) return ({model.Name})r.ReadNumber<{UnderlyingType(model)}>();");
        Line("    var s = r.ReadString();");
        Line("    switch (s)");
        Line("    {");
        foreach (var (name, _) in fields)
            Line($"        case {Literal(name.TrimStart('@'))}: return {model.Name}.{name};");
        Line("    }");

        if (IsFlags(model))
        {
            Line($"    {model.Name} result = default;");
            Line("    foreach (var part in s.Split(','))");
            Line("    {");
            Line("        switch (part.Trim())");
            Line("        {");
            foreach (var (name, _) in fields)
                Line($"            case {Literal(name.TrimStart('@'))}: result |= {model.Name}.{name}; break;");
            Line("            default: throw r.CreateException(\"unknown enum value\");");
            Line("        }");
            Line("    }");
            Line("    return result;");
        }
        else
        {
            Line("    throw r.CreateException(\"unknown enum value\");");
        }
    }

    List<MemberModel> AnalyzeMembers(TypeModel model)
    {
        var levels = new List<List<ISymbol>>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        // collect public instance properties and fields, base types first
        for (var t = model.Symbol as INamedTypeSymbol; t != null && t.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); t = t.BaseType)
        {
            var level = new List<ISymbol>();

            foreach (var member in t.GetMembers())
            {
                if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (member is IPropertySymbol { IsIndexer: false } or IFieldSymbol { IsConst: false, IsImplicitlyDeclared: false })
                {
                    // members hidden or overridden in derived types are skipped
                    if (seenNames.Add(member.Name) && !HasAttribute(member, IgnoreAttributeName))
                        level.Add(member);
                }
            }

            levels.Add(level);
        }

        levels.Reverse();

        var members = new List<MemberModel>();
        var jsonNames = new HashSet<string>(caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var member in levels.SelectMany(l => l))
        {
            var (memberType, canRead, write) = member switch
            {
                IPropertySymbol p => (p.Type,
                    p.GetMethod?.DeclaredAccessibility == Accessibility.Public,
                    p.SetMethod is { DeclaredAccessibility: Accessibility.Public } setter
                        ? (setter.IsInitOnly || p.IsRequired ? WriteKind.Init : WriteKind.Set)
                        : WriteKind.None),
                IFieldSymbol f => (f.Type, true, f.IsReadOnly ? WriteKind.None : f.IsRequired ? WriteKind.Init : WriteKind.Set),
                _ => throw new InvalidOperationException(),
            };

            var location = member.Locations.FirstOrDefault();
            var type = Resolve(memberType, out var reason);

            if (type == null)
            {
                if (reason != null)
                    Report(Diagnostics.UnsupportedMember, location, model.Symbol.ToDisplayString() + "." + member.Name, memberType.ToDisplayString(), reason);
                continue;
            }

            var jsonName = JsonName(member);

            if (!jsonNames.Add(jsonName))
            {
                Report(Diagnostics.DuplicateName, location, member.Name, model.Symbol.ToDisplayString(), jsonName);
                continue;
            }

            members.Add(new MemberModel(member, jsonName, type, canRead, write));
        }

        return members;
    }

    static bool HasAttribute(ISymbol symbol, string name) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == name);

    string JsonName(ISymbol member)
    {
        var attribute = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == PropertyNameAttributeName);

        if (attribute is { ConstructorArguments.Length: 1 } && attribute.ConstructorArguments[0].Value is string name)
            return name;

        return namingPolicy == 1 ? CamelCase(member.Name) : member.Name;
    }

    /// <summary>
    /// Converts a name to camel case the same way as System.Text.Json, e.g. <c>URLValue</c> becomes <c>urlValue</c>.
    /// </summary>
    internal static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
            return name;

        var chars = name.ToCharArray();

        for (int i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;

            bool hasNext = i + 1 < chars.Length;

            // stop at the start of the next word, e.g. the "V" in "URLValue"
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (chars[i + 1] == ' ')
                    chars[i] = char.ToLowerInvariant(chars[i]);
                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    void EmitObjectWrite(TypeModel model, List<MemberModel> members)
    {
        if (model.CanBeNull)
            Line("    if (value is null) { w.WriteNull(); return; }");

        Line("    w.WriteStartObject();");

        var i = 0;
        foreach (var member in members.Where(m => m.CanRead))
        {
            var local = "v" + i++;
            Line($"    var {local} = value.{member.Name};");

            var condition = (ignoreCondition, member.Type.CanBeNull) switch
            {
                (1 or 2, true) => $"{local} is not null",
                (2, false) => $"!global::System.Collections.Generic.EqualityComparer<{member.Type.Name}>.Default.Equals({local}, default)",
                _ => null,
            };

            var indent = condition == null ? "    " : "        ";

            if (condition != null)
            {
                Line($"    if ({condition})");
                Line("    {");
            }

            Line($"{indent}w.WritePropertyName({Literal(member.JsonName)});");
            Line($"{indent}Write_{member.Type.Id}(w, {local});");

            if (condition != null)
                Line("    }");
        }

        Line("    w.WriteEndObject();");
    }

    void EmitObjectRead(TypeModel model, List<MemberModel> members)
    {
        var named = (INamedTypeSymbol)model.Symbol;
        var ctor = FindConstructor(named, members, out var error);

        if (error != null)
        {
            Report(Diagnostics.NotConstructible, named.Locations.FirstOrDefault(), named.ToDisplayString(), error);
            Line($"    throw new global::System.NotSupportedException({Literal("The type '" + named.ToDisplayString() + "' cannot be deserialized: " + error + ".")});");
            return;
        }

        var readMembers = members.Where(m => m.Parameter != null || m.Write != WriteKind.None).ToList();
        var locals = new Dictionary<MemberModel, int>();

        if (model.CanBeNull)
            Line("    if (r.TryReadNull()) return null;");

        for (int i = 0; i < readMembers.Count; i++)
        {
            var member = readMembers[i];
            locals[member] = i;
            Line($"    {member.Type.Name} m{i} = {DefaultValue(member)};");

            // properties with setters are only assigned if present in the JSON, so that defaults from the constructor are preserved
            if (member.Parameter == null && member.Write == WriteKind.Set)
                Line($"    bool h{i} = false;");
        }

        Line("    r.ReadStartObject();");
        Line("    while (r.TryReadPropertyName(out var name))");
        Line("    {");

        if (caseInsensitive)
        {
            var first = true;
            foreach (var member in readMembers)
            {
                var i = locals[member];
                Line($"        {(first ? "if" : "else if")} (global::System.String.Equals(name, {Literal(member.JsonName)}, global::System.StringComparison.OrdinalIgnoreCase)) {{ {ReadAssignment(member, i)} }}");
                first = false;
            }
            Line(first ? "        r.Skip();" : "        else r.Skip();");
        }
        else
        {
            Line("        switch (name)");
            Line("        {");
            foreach (var member in readMembers)
            {
                var i = locals[member];
                Line($"            case {Literal(member.JsonName)}: {ReadAssignment(member, i)} break;");
            }
            Line("            default: r.Skip(); break;");
            Line("        }");
        }

        Line("    }");

        var args = ctor == null
            ? ""
            : string.Join(", ", ctor.Parameters.Select(p => "m" + locals[readMembers.First(m => SymbolEqualityComparer.Default.Equals(m.Parameter, p))]));
        var initializers = readMembers.Where(m => m.Parameter == null && m.Write == WriteKind.Init).Select(m => $"{m.Name} = m{locals[m]}").ToList();

        Line(initializers.Count == 0
            ? $"    var v = new {model.Name}({args});"
            : $"    var v = new {model.Name}({args}) {{ {string.Join(", ", initializers)} }};");

        foreach (var member in readMembers.Where(m => m.Parameter == null && m.Write == WriteKind.Set))
        {
            var i = locals[member];
            Line($"    if (h{i}) v.{member.Name} = m{i};");
        }

        Line("    return v;");
    }

    static string ReadAssignment(MemberModel member, int i) =>
        member.Parameter == null && member.Write == WriteKind.Set
            ? $"m{i} = Read_{member.Type.Id}(r); h{i} = true;"
            : $"m{i} = Read_{member.Type.Id}(r);";

    static string DefaultValue(MemberModel member)
    {
        var p = member.Parameter;

        if (p is { HasExplicitDefaultValue: true, ExplicitDefaultValue: not null })
        {
            var literal = p.ExplicitDefaultValue switch
            {
                string s => SymbolDisplay.FormatLiteral(s, quote: true),
                char c => SymbolDisplay.FormatLiteral(c, quote: true),
                bool b => b ? "true" : "false",
                var v => SymbolDisplay.FormatPrimitive(v, quoteStrings: true, useHexadecimalNumbers: false),
            };

            return $"({member.Type.Name})({literal})";
        }

        return "default";
    }

    /// <summary>
    /// Finds the constructor to use for deserialization. Returns <c>null</c> (and no error) if the parameterless constructor should be used.
    /// </summary>
    IMethodSymbol? FindConstructor(INamedTypeSymbol type, List<MemberModel> members, out string? error)
    {
        error = null;

        if (type.IsAbstract)
        {
            error = "it is abstract";
            return null;
        }

        var publicConstructors = type.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToList();
        IMethodSymbol? ctor;

        if (type.TypeKind == TypeKind.Struct)
        {
            // structs always have a parameterless constructor; use a single constructor with parameters (e.g. of a record struct) if there is one
            var withParameters = publicConstructors.Where(c => c.Parameters.Length > 0).ToList();
            ctor = withParameters.Count == 1 ? withParameters[0] : null;
        }
        else if (publicConstructors.Any(c => c.Parameters.Length == 0))
        {
            ctor = null;
        }
        else if (publicConstructors.Count == 1)
        {
            ctor = publicConstructors[0];
        }
        else
        {
            error = publicConstructors.Count == 0
                ? "it has no public constructor"
                : "it has several public constructors and none without parameters";
            return null;
        }

        if (ctor == null)
            return null;

        foreach (var parameter in ctor.Parameters)
        {
            var member = members.FirstOrDefault(m => string.Equals(m.Symbol.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));

            if (member == null || !SymbolEqualityComparer.Default.Equals(member.Type.Symbol, parameter.Type))
            {
                error = "the constructor parameter '" + parameter.Name + "' does not match a serializable property or field";
                return null;
            }

            member.Parameter = parameter;
        }

        return ctor;
    }

    string BuildSource(List<TypeModel> roots)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable disable");
        sb.AppendLine("#pragma warning disable CS0612, CS0618, CS1522");
        sb.AppendLine();

        var ns = context.ContainingNamespace.IsGlobalNamespace ? null : context.ContainingNamespace.ToDisplayString();

        if (ns != null)
        {
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
        }

        var containers = new List<INamedTypeSymbol>();
        for (var t = context; t != null; t = t.ContainingType)
            containers.Insert(0, t);

        foreach (var t in containers)
        {
            var keyword = t.IsRecord
                ? (t.TypeKind == TypeKind.Struct ? "record struct" : "record")
                : t.TypeKind switch { TypeKind.Struct => "struct", TypeKind.Interface => "interface", _ => "class" };
            sb.AppendLine((t.IsStatic ? "static " : "") + "partial " + keyword + " " + Identifier(t.Name));
            sb.AppendLine("{");
        }

        foreach (var root in roots)
        {
            var id = root.Id;
            sb.AppendLine("        /// <summary>Serializes a value to JSON.</summary>");
            sb.AppendLine($"        public static string Serialize({root.Name} value)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var w = new {Writer}();");
            sb.AppendLine($"            Write_{id}(w, value);");
            sb.AppendLine("            return w.ToString();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Writes a value as JSON.</summary>");
            sb.AppendLine($"        public static void Serialize({Writer} writer, {root.Name} value) => Write_{id}(writer, value);");
            sb.AppendLine();
        }

        sb.AppendLine("        /// <summary>Deserializes JSON text into a value of a type that is registered through [MicroJsonSerializable] on this class.</summary>");
        sb.AppendLine("        public static T Deserialize<T>(string json)");
        sb.AppendLine("        {");
        sb.AppendLine($"            var r = new {Reader}(json);");
        sb.AppendLine("            var value = Deserialize<T>(r);");
        sb.AppendLine("            r.ReadEndOfInput();");
        sb.AppendLine("            return value;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>Reads a value of a type that is registered through [MicroJsonSerializable] on this class.</summary>");
        sb.AppendLine($"        public static T Deserialize<T>({Reader} reader)");
        sb.AppendLine("        {");
        foreach (var root in roots)
            sb.AppendLine($"            if (typeof(T) == typeof({root.Name})) return (T)(object)Read_{root.Id}(reader);");
        sb.AppendLine("            throw new global::System.NotSupportedException(\"The type is not registered through [MicroJsonSerializable] on this class.\");");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.Append(code);

        for (int i = 0; i < containers.Count; i++)
            sb.AppendLine("}");

        if (ns != null)
            sb.AppendLine("}");

        return sb.ToString();
    }
}
