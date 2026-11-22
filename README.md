MicroJson
=========

[![NuGet version](https://badge.fury.io/nu/MicroJson.svg)](https://badge.fury.io/nu/MicroJson)
[![Build status](https://github.com/mganss/MicroJson/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/mganss/MicroJson/actions/workflows/build.yml)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=mganss_MicroJson&metric=coverage)](https://sonarcloud.io/dashboard?id=mganss_MicroJson)
[![Sonarcloud Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=mganss_MicroJson&metric=alert_status)](https://sonarcloud.io/dashboard?id=mganss_MicroJson)

[![net10.0](https://img.shields.io/badge/net-10.0-brightgreen.svg)](https://img.shields.io/badge/net-10.0-brightgreen.svg)

MicroJson is a minimal [JSON](https://www.json.org/) library for .NET, optimized for [Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
and trimming. Use it where you need JSON support and want the smallest binary possible.

Serialization code is generated at compile time by a source generator that ships in the package. There is no reflection
at runtime, so there are no trimming or AOT warnings, and only the code your types actually need ends up in your binary.

Binary size
-----------

Size added by the same small program (deserialize an object with a string, an int, and a list of strings, modify it,
and serialize it back) to a Native AOT hello world, measured on win-x64 with .NET 10, `InvariantGlobalization`, and `UseSystemResourceKeys`:

| | Added to baseline |
|---|---:|
| MicroJson (source-generated serialization) | 22 KB |
| MicroJson (`JsonParser` and `JsonWriter.WriteValue`) | 150 KB |
| MicroJson (`JsonParser` and `JsonWriter.WriteValue`, `MicroJson.UseDecimal` off) | 71.5 KB |
| System.Text.Json (source-generated serialization) | 1237 KB |

The sample apps are in [samples/AotSize](samples/AotSize).

Tips for small binaries:

- Prefer the source generator over `JsonParser`. If you use `JsonParser`, consider turning off `decimal` numbers (see below):
  a boxed `decimal` costs about 75 KB, because through `decimal`'s `IConvertible` implementation it pulls in enum formatting
  and the reflection code that uses.
- Only the types you use cost space, and some of .NET's own code is large: `decimal` and `double` parsing and formatting
  add tens of KB, while formatting a `DateTime` as ISO 8601 adds about 230 KB and parsing one about 260 KB
  (measured without MicroJson). If size matters most, prefer e.g. Unix timestamps in a `long` over date/time types.
- Set `InvariantGlobalization` and `UseSystemResourceKeys` in your project.

Serialization
-------------

Register the types you want to serialize on a `partial` class:

```csharp
using MicroJson;

public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public List<string> Tags { get; set; } = [];
}

[MicroJsonSerializable(typeof(Person))]
partial class AppJson { }
```

The generator adds static methods to the class:

```csharp
var json = AppJson.Serialize(new Person { Name = "Ann", Age = 42 });  // {"Name":"Ann","Age":42,"Tags":[]}
var person = AppJson.Deserialize<Person>(json);
```

`Serialize(JsonWriter, T)` and `Deserialize<T>(JsonReader)` overloads let you embed values in JSON you write or read yourself.
Types referenced by registered types (properties, collection elements, etc.) are handled automatically; only the types
you call `Serialize`/`Deserialize` with need to be registered.

### Supported types

- `string`, `bool`, `char`, all integer types, `float`, `double`, `decimal`
- `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` (ISO 8601), `TimeSpan`, `Guid`, `Uri`
- enums (as names; `[Flags]` combinations as `"A, B"`; numbers are accepted when reading)
- `Nullable<T>`
- arrays, `List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`
- `Dictionary<string, TValue>`, `IDictionary<string, TValue>`, `IReadOnlyDictionary<string, TValue>`
- `object` (read and written as generic CLR objects, see `JsonParser` below)
- classes, structs, and records with public properties and fields

Objects are created through a public parameterless constructor or, if there is none, through the type's single public
constructor (e.g. a record's primary constructor) whose parameters are matched to properties by name.
`init` and `required` properties are supported. Properties with setters keep their default values if they are missing from the JSON.

Unsupported types and members are reported as compiler diagnostics (`MJ0001`–`MJ0007`).

### Options

```csharp
[MicroJsonOptions(
    PropertyNamingPolicy = MicroJsonNamingPolicy.CamelCase,          // FirstName -> firstName
    DefaultIgnoreCondition = MicroJsonIgnoreCondition.WhenWritingNull, // or WhenWritingDefault
    PropertyNameCaseInsensitive = true)]
[MicroJsonSerializable(typeof(Person))]
partial class AppJson { }

public class Product
{
    [MicroJsonPropertyName("product-id")] public int Id { get; set; }
    [MicroJsonIgnore] public string? Internal { get; set; }
}
```

### Polymorphism

Register the derived types of a class or interface with a discriminator. When a value is serialized as the base type, the discriminator
is written as the first property:

```csharp
[MicroJsonDerivedType(typeof(Dog), "dog")]
[MicroJsonDerivedType(typeof(Cat), "cat")]
public abstract class Animal { public string Name { get; set; } = ""; }
public class Dog : Animal { public bool GoodBoy { get; set; } }
public class Cat : Animal { public int Lives { get; set; } = 9; }

[MicroJsonSerializable(typeof(Animal))]
partial class AppJson { }

AppJson.Serialize((Animal)new Dog { Name = "Rex" });  // {"$type":"dog","Name":"Rex","GoodBoy":false}
var animal = AppJson.Deserialize<Animal>("""{"$type":"cat","Name":"Tom"}""");  // Cat
```

- The discriminator property is named `$type`; use `[MicroJsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` on the base type to change it
  (`"@type"` and class names as discriminators match the format of MicroJson 1.x's `UseTypeInfo`).
- When reading, the discriminator must be the first property. Without it, the base type itself is read, which fails if it is abstract or an interface.
- A value of a type that is not registered is written as its nearest registered base type. If there is none and the base type is abstract
  or an interface, `Serialize` throws `NotSupportedException`.
- Polymorphism applies where the declared type is the annotated base type, e.g. a `List<Animal>` or a property of type `Animal`.
  A value declared as `Dog` is written without a discriminator.

Parsing to generic objects
--------------------------

`JsonParser` parses JSON text into generic CLR objects: JSON objects become `IDictionary<string, object?>`,
arrays become `IList<object?>`, strings become `string`, numbers become `int`, `long`, `ulong`, `decimal` or `double`,
and `true`/`false`/`null` become `bool` and `null`.

```csharp
using MicroJson;

var json = """
    {
        "S": "Hello, world.",
        "I": 4711,
        "L": [1, 2, 3]
    }
    """;

var o = (IDictionary<string, object?>)new JsonParser().Parse(json)!;

Console.WriteLine(o["S"]);                  // Hello, world.
Console.WriteLine(o["I"]);                  // 4711
Console.WriteLine(((IList<object?>)o["L"]!).Count); // 3
```

Integers become `int`, `long`, or `ulong` (the smallest that fits), other numbers become `decimal` or, if out of
`decimal`'s range, `double`. Numbers outside of `double`'s range are rejected. Precision beyond what the type can hold
(28–29 significant digits for `decimal`, 15–17 for `double`) is rounded, and numbers too small for the type become 0.

To keep such numbers exactly, set `PreserveInexactNumbers`. Numbers that would be rounded, become 0, or be rejected as out of range
are then returned as `JsonNumber`, which holds the original text (`Text`, `TryGetDouble`, `TryGetDecimal`).
`JsonWriter.WriteValue` writes a `JsonNumber` unchanged, so the numbers survive a round trip:

```csharp
var parser = new JsonParser { PreserveInexactNumbers = true };
var values = (IList<object?>)parser.Parse("[0.1, 1e400, 123456789012345678901234567890]")!;
// decimal 0.1, JsonNumber "1e400", JsonNumber "123456789012345678901234567890"
```

A number is kept as `JsonNumber` only if no other type holds all of its digits: with `decimal`, up to 28–29 significant digits and
28 decimal places; otherwise (and with `MicroJson.UseDecimal` off) `double` with up to 15 significant digits.

In generated serialization code, numbers are read directly into the member's type. A number that doesn't fit the type is
rejected, as is a fraction read into an integer type (`1.0` and `1e3` are accepted for integers).

To get `double` instead of `decimal`, turn off the `MicroJson.UseDecimal` feature switch in your project file.
With `Trim="true"`, trimming and Native AOT remove the `decimal` code, which makes the binary about 75 KB smaller:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="MicroJson.UseDecimal" Value="false" Trim="true" />
</ItemGroup>
```

To write generic objects back to JSON, use `JsonWriter.WriteValue`:

```csharp
var writer = new JsonWriter();
writer.WriteValue(o);
Console.WriteLine(writer.ToString());       // {"S":"Hello, world.","I":4711,"L":[1,2,3]}
```

Set `CollectLineInfo` to `true` to get line and column numbers in `ParserException`.

Reading and writing
-------------------

`JsonReader` and `JsonWriter` are the low-level forward-only reader and writer that the generated code and `JsonParser` are built on.
You can also use them directly.

Nesting of arrays and objects is limited to `MaxDepth` levels (default 64) to protect against stack exhaustion
from malicious input.

The reader is strict [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259) and passes all accept/reject cases of
[JSONTestSuite](https://github.com/nst/JSONTestSuite). All errors are reported as `ParserException`.
The writer escapes strings correctly and rejects NaN and infinity, which JSON cannot represent.

License
-------

[MIT](LICENSE)

Used at
---------

[UpdateStar](https://www.updatestar.com/)
