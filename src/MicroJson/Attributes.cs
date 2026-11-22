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
/// Registers a derived type of the annotated class or interface for polymorphic serialization.
/// </summary>
/// <remarks>
/// When a value whose declared type is the annotated type is serialized, its runtime type's discriminator is written as the first property
/// (named <c>$type</c> unless changed through <see cref="MicroJsonPolymorphicAttribute"/>), followed by the runtime type's properties.
/// A value whose runtime type is not registered is written as its nearest registered base type; if there is none and the annotated type is
/// abstract or an interface, serialization throws <see cref="NotSupportedException"/>.
/// When deserializing, the discriminator must be the first property of the object. Without a discriminator, the annotated type itself
/// is deserialized (which fails if it is abstract or an interface).
/// </remarks>
/// <param name="derivedType">The derived type, a non-abstract class or struct that derives from or implements the annotated type.</param>
/// <param name="typeDiscriminator">The value that identifies the derived type in JSON.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("MICROJSON_KEEP_ATTRIBUTES")]
public sealed class MicroJsonDerivedTypeAttribute(Type derivedType, string typeDiscriminator) : Attribute
{
    /// <summary>
    /// Gets the derived type.
    /// </summary>
    public Type DerivedType { get; } = derivedType;

    /// <summary>
    /// Gets the value that identifies the derived type in JSON.
    /// </summary>
    public string TypeDiscriminator { get; } = typeDiscriminator;
}

/// <summary>
/// Configures polymorphic serialization of a class or interface with derived types registered through <see cref="MicroJsonDerivedTypeAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
[Conditional("MICROJSON_KEEP_ATTRIBUTES")]
public sealed class MicroJsonPolymorphicAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name of the property that holds the type discriminator. The default is <c>$type</c>.
    /// </summary>
    public string TypeDiscriminatorPropertyName { get; set; } = "$type";
}

/// <summary>
/// Excludes a property or field from serialization and deserialization.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
[Conditional("MICROJSON_KEEP_ATTRIBUTES")]
public sealed class MicroJsonIgnoreAttribute : Attribute
{
}
