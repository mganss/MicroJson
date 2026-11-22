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

> [!NOTE]
> MicroJson 2.0 is a rewrite. The reflection-based `JsonSerializer` from 1.x has been removed because it is
> incompatible with trimming and Native AOT; use the source generator described below instead.
> The 1.x releases remain available on [NuGet](https://www.nuget.org/packages/MicroJson).

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

Unsupported types and members are reported as compiler diagnostics (`MJ0001`–`MJ0006`).

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

Parsing to generic objects
--------------------------

`JsonParser` parses JSON text into generic CLR objects: JSON objects become `IDictionary<string, object?>`,
arrays become `IList<object?>`, strings become `string`, numbers become `int`, `long`, `decimal` or `double`,
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

Integers become `int` or `long` if they fit, other numbers become `decimal` or, if out of `decimal`'s range, `double`.
Numbers outside of `double`'s range are rejected.

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
