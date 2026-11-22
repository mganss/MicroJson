using System;
using System.Diagnostics;

namespace MicroJson;

// The attributes are only read by the source generator at compile time. They are [Conditional] so that they are not
// emitted into the metadata of the user's assembly, which keeps the binary smaller.

/// <summary>
/// Generates serialization code for the specified type (and all types it references) into the annotated <c>partial</c> class.
/// </summary>
/// <remarks>
/// The class gets the static methods <c>Serialize(T)</c>, <c>Serialize(JsonWriter, T)</c>, <c>Deserialize&lt;T&gt;(string)</c>,
/// and <c>Deserialize&lt;T&gt;(JsonReader)</c> for each type specified through this attribute.
/// </remarks>
/// <param name="type">The type to serialize.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
[Conditional("MICROJSON_KEEP_ATTRIBUTES")]
public sealed class MicroJsonSerializableAttribute(Type type) : Attribute
{
    /// <summary>
    /// Gets the type to serialize.
    /// </summary>
    public Type Type { get; } = type;
}

/// <summary>
/// Configures the code generated into a class annotated with <see cref="MicroJsonSerializableAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[Conditional("MICROJSON_KEEP_ATTRIBUTES")]
public sealed class MicroJsonOptionsAttribute : Attribute
{
    /// <summary>
    /// Gets or sets how property names are converted to JSON names. The default is <see cref="MicroJsonNamingPolicy.None"/>.
    /// </summary>
    public MicroJsonNamingPolicy PropertyNamingPolicy { get; set; }

    /// <summary>
    /// Gets or sets when properties are omitted during serialization. The default is <see cref="MicroJsonIgnoreCondition.Never"/>.
    /// </summary>
    public MicroJsonIgnoreCondition DefaultIgnoreCondition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether property names are matched case-insensitively during deserialization.
    /// The default is <c>false</c>.
    /// </summary>
    public bool PropertyNameCaseInsensitive { get; set; }
}

/// <summary>
/// Specifies how property names are converted to JSON names.
/// </summary>
public enum MicroJsonNamingPolicy
{
    /// <summary>Use the property names as they are.</summary>
    None,
    /// <summary>Convert the first character to lower case, e.g. <c>FirstName</c> becomes <c>firstName</c>.</summary>
    CamelCase,
}

/// <summary>
/// Specifies when properties are omitted during serialization.
/// </summary>
public enum MicroJsonIgnoreCondition
{
    /// <summary>Always write properties.</summary>
    Never,
    /// <summary>Omit properties whose value is <c>null</c>.</summary>
    WhenWritingNull,
    /// <summary>Omit properties whose value is the default value of their type (e.g. <c>null</c>, <c>0</c>, or <c>false</c>).</summary>
    WhenWritingDefault,
}

/// <summary>
/// Specifies the JSON name of a property or field.
/// </summary>
/// <param name="name">The JSON name.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
[Conditional("MICROJSON_KEEP_ATTRIBUTES")]
public sealed class MicroJsonPropertyNameAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the JSON name.
    /// </summary>
    public string Name { get; } = name;
}

/// <summary>
/// Excludes a property or field from serialization and deserialization.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
[Conditional("MICROJSON_KEEP_ATTRIBUTES")]
public sealed class MicroJsonIgnoreAttribute : Attribute
{
}
