MicroJson
=========

[![NuGet version](https://badge.fury.io/nu/MicroJson.svg)](https://badge.fury.io/nu/MicroJson)
[![Build status](https://github.com/mganss/MicroJson/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/mganss/MicroJson/actions/workflows/build.yml)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=mganss_MicroJson&metric=coverage)](https://sonarcloud.io/dashboard?id=mganss_MicroJson)
[![Sonarcloud Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=mganss_MicroJson&metric=alert_status)](https://sonarcloud.io/dashboard?id=mganss_MicroJson)

[![net10.0](https://img.shields.io/badge/net-10.0-brightgreen.svg)](https://img.shields.io/badge/net-10.0-brightgreen.svg)

MicroJson is a minimal [JSON](https://www.json.org/) library for .NET, optimized for [Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
and trimming. Use it where you need JSON support and want the smallest binary possible.

> [!NOTE]
> MicroJson 2.0 is under development. The reflection-based `JsonSerializer` from 1.x has been removed because it is
> incompatible with trimming and Native AOT. Typed serialization will return in 2.0 via a source generator.
> The 1.x releases remain available on [NuGet](https://www.nuget.org/packages/MicroJson).

Usage
-----

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

Set `CollectLineInfo` to `true` to get line and column numbers in `ParserException`.

License
-------

[MIT](LICENSE)

Used at
---------

[UpdateStar](https://www.updatestar.com/)
