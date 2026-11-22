#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MicroJson.Tests
{
    public enum Color { Red, Green, Blue }

    [Flags]
    public enum Permissions { None = 0, Read = 1, Write = 2, Execute = 4, ReadWrite = Read | Write }

    public enum Small : byte { A = 1, B = 200 }

    public class Address
    {
        public string Street { get; set; } = "";
        public string? City;
    }

    public class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        [MicroJsonPropertyName("e-mail")]
        public string? Email { get; set; }
        public List<string> Tags { get; set; } = [];
        public Address? Address { get; set; }
        public Color FavoriteColor { get; set; }
        public int Default { get; set; } = 4711;
        public string Computed => "computed";
        [MicroJsonIgnore]
        public string Ignored { get; set; } = "ignored";
        public bool Active;
    }

    public record Point(int X, int Y);

    public record struct Size(double Width, double Height = 1.5);

    public class WithInit
    {
        public required string Id { get; init; }
        public int Count { get; init; }
    }

    public class WithConstructor(string name, Color color = Color.Blue)
    {
        public string Name { get; } = name;
        public Color Color { get; } = color;
        public int Extra { get; set; } = 42;
    }

    public class AllTypes
    {
        public string? S { get; set; }
        public bool B { get; set; }
        public char C { get; set; }
        public sbyte SB { get; set; }
        public byte By { get; set; }
        public short Sh { get; set; }
        public ushort US { get; set; }
        public int I { get; set; }
        public uint UI { get; set; }
        public long L { get; set; }
        public ulong UL { get; set; }
        public float F { get; set; }
        public double D { get; set; }
        public decimal M { get; set; }
        public DateTime DT { get; set; }
        public DateTimeOffset DTO { get; set; }
        public DateOnly DO { get; set; }
        public TimeOnly TO { get; set; }
        public TimeSpan TS { get; set; }
        public Guid G { get; set; }
        public Uri? U { get; set; }
        public object? Dom { get; set; }
        public int? N { get; set; }
        public Color? NC { get; set; }
        public int[]? Arr { get; set; }
        public IList<int>? IL { get; set; }
        public IEnumerable<string>? IE { get; set; }
        public IReadOnlyList<long>? IRL { get; set; }
        public Dictionary<string, int>? Dict { get; set; }
        public IReadOnlyDictionary<string, Address>? IRD { get; set; }
        public Permissions P { get; set; }
        public Small Sm { get; set; }
        public List<List<int>>? Nested { get; set; }
    }

    public class Node
    {
        public int Value;
        public List<Node>? Children;
    }

    public class Base
    {
        public int BaseValue { get; set; }
        public virtual string Name { get; set; } = "base";
    }

    public class Derived : Base
    {
        public int DerivedValue { get; set; }
        public override string Name { get; set; } = "derived";
    }

    public class Keywords
    {
        public int @class { get; set; }
        public string? URLValue { get; set; }
    }

    public struct MutableStruct
    {
        public int A { get; set; }
        public string? B;
    }

    [MicroJsonSerializable(typeof(Person))]
    [MicroJsonSerializable(typeof(Point))]
    [MicroJsonSerializable(typeof(Size))]
    [MicroJsonSerializable(typeof(WithInit))]
    [MicroJsonSerializable(typeof(WithConstructor))]
    [MicroJsonSerializable(typeof(AllTypes))]
    [MicroJsonSerializable(typeof(Node))]
    [MicroJsonSerializable(typeof(Derived))]
    [MicroJsonSerializable(typeof(Keywords))]
    [MicroJsonSerializable(typeof(MutableStruct))]
    [MicroJsonSerializable(typeof(List<Point>))]
    [MicroJsonSerializable(typeof(Dictionary<string, Color>))]
    [MicroJsonSerializable(typeof(Permissions))]
    [MicroJsonSerializable(typeof(int))]
    [MicroJsonSerializable(typeof(int?))]
    [MicroJsonSerializable(typeof(string))]
    internal partial class TestJson
    {
    }

    [MicroJsonOptions(PropertyNamingPolicy = MicroJsonNamingPolicy.CamelCase, DefaultIgnoreCondition = MicroJsonIgnoreCondition.WhenWritingDefault,
        PropertyNameCaseInsensitive = true)]
    [MicroJsonSerializable(typeof(Person))]
    [MicroJsonSerializable(typeof(Keywords))]
    internal partial class CamelJson
    {
    }

    [MicroJsonOptions(DefaultIgnoreCondition = MicroJsonIgnoreCondition.WhenWritingNull)]
    [MicroJsonSerializable(typeof(AllTypes))]
    internal partial class IgnoreNullJson
    {
    }

    internal static partial class Outer
    {
        [MicroJsonSerializable(typeof(Point))]
        internal partial class Inner
        {
        }
    }

    [TestFixture]
    public class SerializationTests
    {
        [Test]
        public void TestPersonRoundTrip()
        {
            var person = new Person
            {
                Name = "Ann \"A\"",
                Age = 42,
                Email = "ann@example.com",
                Tags = ["a", "b"],
                Address = new Address { Street = "Main St", City = null },
                FavoriteColor = Color.Green,
                Default = 1,
                Active = true,
            };

            var json = TestJson.Serialize(person);

            Assert.That(json, Is.EqualTo("""{"Name":"Ann \"A\"","Age":42,"e-mail":"ann@example.com","Tags":["a","b"],"Address":{"Street":"Main St","City":null},"FavoriteColor":"Green","Default":1,"Computed":"computed","Active":true}"""));

            var p = TestJson.Deserialize<Person>(json);

            Assert.That(p.Name, Is.EqualTo(person.Name));
            Assert.That(p.Age, Is.EqualTo(42));
            Assert.That(p.Email, Is.EqualTo(person.Email));
            Assert.That(p.Tags, Is.EqualTo(person.Tags));
            Assert.That(p.Address!.Street, Is.EqualTo("Main St"));
            Assert.That(p.Address.City, Is.Null);
            Assert.That(p.FavoriteColor, Is.EqualTo(Color.Green));
            Assert.That(p.Default, Is.EqualTo(1));
            Assert.That(p.Ignored, Is.EqualTo("ignored"));
            Assert.That(p.Active, Is.True);
        }

        [Test]
        public void TestMissingPropertiesKeepDefaults()
        {
            var p = TestJson.Deserialize<Person>("{}");

            Assert.That(p.Default, Is.EqualTo(4711));
            Assert.That(p.Name, Is.EqualTo(""));
            Assert.That(p.Tags, Is.Empty);
        }

        [Test]
        public void TestUnknownPropertiesAreSkipped()
        {
            var p = TestJson.Deserialize<Person>("""{"x":{"a":[1,2,{"b":null}],"c":"d"},"Age":3,"Ignored":"no","Computed":"no","y":[true,false,-1.5e3]}""");

            Assert.That(p.Age, Is.EqualTo(3));
            Assert.That(p.Ignored, Is.EqualTo("ignored"));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>("""{"x":[1,}"""));
        }

        [Test]
        public void TestNulls()
        {
            Assert.That(TestJson.Serialize((Person)null!), Is.EqualTo("null"));
            Assert.That(TestJson.Deserialize<Person>("null"), Is.Null);
            Assert.That(TestJson.Deserialize<Person>("""{"Address":null,"e-mail":null}""").Address, Is.Null);
            Assert.That(TestJson.Serialize((string?)null), Is.EqualTo("null"));
            Assert.That(TestJson.Deserialize<string>("null"), Is.Null);
            Assert.That(TestJson.Serialize((int?)null), Is.EqualTo("null"));
            Assert.That(TestJson.Serialize((int?)5), Is.EqualTo("5"));
            Assert.That(TestJson.Deserialize<int?>("null"), Is.Null);
            Assert.That(TestJson.Deserialize<int?>("7"), Is.EqualTo(7));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<int>("null"));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<MutableStruct>("null"));
        }

        [Test]
        public void TestRecords()
        {
            Assert.That(TestJson.Serialize(new Point(1, -2)), Is.EqualTo("""{"X":1,"Y":-2}"""));
            Assert.That(TestJson.Deserialize<Point>("""{"Y":-2,"X":1}"""), Is.EqualTo(new Point(1, -2)));
            Assert.That(TestJson.Deserialize<Point>("{}"), Is.EqualTo(new Point(0, 0)));

            Assert.That(TestJson.Serialize(new Size(2, 3.25)), Is.EqualTo("""{"Width":2,"Height":3.25}"""));
            Assert.That(TestJson.Deserialize<Size>("""{"Width":2,"Height":3.25}"""), Is.EqualTo(new Size(2, 3.25)));
            Assert.That(TestJson.Deserialize<Size>("""{"Width":2}"""), Is.EqualTo(new Size(2, 1.5)));

            var points = TestJson.Deserialize<List<Point>>("""[{"X":1,"Y":2},{"X":3,"Y":4}]""");
            Assert.That(points, Is.EqualTo(new List<Point> { new(1, 2), new(3, 4) }));
            Assert.That(TestJson.Serialize(points), Is.EqualTo("""[{"X":1,"Y":2},{"X":3,"Y":4}]"""));

            Assert.That(Outer.Inner.Deserialize<Point>(Outer.Inner.Serialize(new Point(5, 6))), Is.EqualTo(new Point(5, 6)));
        }

        [Test]
        public void TestInitAndRequired()
        {
            var w = TestJson.Deserialize<WithInit>("""{"Id":"a","Count":3}""");
            Assert.That(w.Id, Is.EqualTo("a"));
            Assert.That(w.Count, Is.EqualTo(3));
            Assert.That(TestJson.Serialize(w), Is.EqualTo("""{"Id":"a","Count":3}"""));
        }

        [Test]
        public void TestConstructorParameters()
        {
            var w = TestJson.Deserialize<WithConstructor>("""{"Name":"n","Color":"Red","Extra":1}""");
            Assert.That(w.Name, Is.EqualTo("n"));
            Assert.That(w.Color, Is.EqualTo(Color.Red));
            Assert.That(w.Extra, Is.EqualTo(1));

            // default value of the constructor parameter and of the property
            w = TestJson.Deserialize<WithConstructor>("""{"Name":"n"}""");
            Assert.That(w.Color, Is.EqualTo(Color.Blue));
            Assert.That(w.Extra, Is.EqualTo(42));
        }

        static AllTypes CreateAllTypes() => new()
        {
            S = "s\n\t\u0001\\/",
            B = true,
            C = 'x',
            SB = -8,
            By = 255,
            Sh = -16000,
            US = 65000,
            I = int.MinValue,
            UI = uint.MaxValue,
            L = long.MinValue,
            UL = ulong.MaxValue,
            F = 1.25f,
            D = 0.1,
            M = 79228162514264337593543950335m,
            DT = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            DTO = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)),
            DO = new DateOnly(2024, 12, 31),
            TO = new TimeOnly(23, 59, 58),
            TS = new TimeSpan(1, 2, 3, 4),
            G = new Guid("01234567-89ab-cdef-0123-456789abcdef"),
            U = new Uri("https://example.com/a?b=c"),
            Dom = new Dictionary<string, object?> { ["a"] = new List<object?> { 1, "x", null } },
            N = 3,
            NC = Color.Blue,
            Arr = [1, 2],
            IL = [3],
            IE = ["e"],
            IRL = [4L],
            Dict = new() { ["k"] = 1 },
            IRD = new Dictionary<string, Address> { ["home"] = new() { Street = "s" } },
            P = Permissions.Read | Permissions.Execute,
            Sm = Small.B,
            Nested = [[1], []],
        };

        [Test]
        public void TestAllTypes()
        {
            var json = TestJson.Serialize(CreateAllTypes());

            Assert.That(json, Is.EqualTo(
                "{\"S\":\"s\\n\\t\\u0001\\\\/\",\"B\":true,\"C\":\"x\",\"SB\":-8,\"By\":255,\"Sh\":-16000,\"US\":65000,\"I\":-2147483648,\"UI\":4294967295," +
                "\"L\":-9223372036854775808,\"UL\":18446744073709551615,\"F\":1.25,\"D\":0.1,\"M\":79228162514264337593543950335," +
                "\"DT\":\"2024-01-02T03:04:05.0000000Z\",\"DTO\":\"2024-01-02T03:04:05.0000000+02:00\",\"DO\":\"2024-12-31\",\"TO\":\"23:59:58.0000000\"," +
                "\"TS\":\"1.02:03:04\",\"G\":\"01234567-89ab-cdef-0123-456789abcdef\",\"U\":\"https://example.com/a?b=c\",\"Dom\":{\"a\":[1,\"x\",null]}," +
                "\"N\":3,\"NC\":\"Blue\",\"Arr\":[1,2],\"IL\":[3],\"IE\":[\"e\"],\"IRL\":[4],\"Dict\":{\"k\":1},\"IRD\":{\"home\":{\"Street\":\"s\",\"City\":null}}," +
                "\"P\":\"Read, Execute\",\"Sm\":\"B\",\"Nested\":[[1],[]]}"));

            var a = TestJson.Deserialize<AllTypes>(json);
            var e = CreateAllTypes();

            Assert.That(a.S, Is.EqualTo(e.S));
            Assert.That(a.B, Is.EqualTo(e.B));
            Assert.That(a.C, Is.EqualTo(e.C));
            Assert.That(a.SB, Is.EqualTo(e.SB));
            Assert.That(a.By, Is.EqualTo(e.By));
            Assert.That(a.Sh, Is.EqualTo(e.Sh));
            Assert.That(a.US, Is.EqualTo(e.US));
            Assert.That(a.I, Is.EqualTo(e.I));
            Assert.That(a.UI, Is.EqualTo(e.UI));
            Assert.That(a.L, Is.EqualTo(e.L));
            Assert.That(a.UL, Is.EqualTo(e.UL));
            Assert.That(a.F, Is.EqualTo(e.F));
            Assert.That(a.D, Is.EqualTo(e.D));
            Assert.That(a.M, Is.EqualTo(e.M));
            Assert.That(a.DT, Is.EqualTo(e.DT));
            Assert.That(a.DT.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(a.DTO, Is.EqualTo(e.DTO));
            Assert.That(a.DTO.Offset, Is.EqualTo(e.DTO.Offset));
            Assert.That(a.DO, Is.EqualTo(e.DO));
            Assert.That(a.TO, Is.EqualTo(e.TO));
            Assert.That(a.TS, Is.EqualTo(e.TS));
            Assert.That(a.G, Is.EqualTo(e.G));
            Assert.That(a.U, Is.EqualTo(e.U));
            Assert.That(a.Dom, Is.EqualTo(e.Dom));
            Assert.That(a.N, Is.EqualTo(e.N));
            Assert.That(a.NC, Is.EqualTo(e.NC));
            Assert.That(a.Arr, Is.EqualTo(e.Arr));
            Assert.That(a.IL, Is.EqualTo(e.IL));
            Assert.That(a.IE, Is.EqualTo(e.IE));
            Assert.That(a.IRL, Is.EqualTo(e.IRL));
            Assert.That(a.Dict, Is.EqualTo(e.Dict));
            Assert.That(a.IRD!["home"].Street, Is.EqualTo("s"));
            Assert.That(a.P, Is.EqualTo(e.P));
            Assert.That(a.Sm, Is.EqualTo(e.Sm));
            Assert.That(a.Nested, Is.EqualTo(e.Nested));
        }

        [Test]
        public void TestDefaultsAndNulls()
        {
            var json = TestJson.Serialize(new AllTypes());
            Assert.That(json, Does.Contain("\"S\":null"));
            Assert.That(json, Does.Contain("\"I\":0"));
            Assert.That(json, Does.Contain("\"N\":null"));
            Assert.That(json, Does.Contain("\"P\":\"None\""));

            var a = TestJson.Deserialize<AllTypes>(json);
            Assert.That(a.S, Is.Null);
            Assert.That(a.Arr, Is.Null);
            Assert.That(a.N, Is.Null);
            Assert.That(a.U, Is.Null);
            Assert.That(a.Dom, Is.Null);

            var ignoreNull = IgnoreNullJson.Serialize(new AllTypes());
            Assert.That(ignoreNull, Does.Not.Contain("null"));
            Assert.That(ignoreNull, Does.Contain("\"I\":0"));
        }

        [Test]
        public void TestEnums()
        {
            Assert.That(TestJson.Serialize(Permissions.None), Is.EqualTo("\"None\""));
            Assert.That(TestJson.Serialize(Permissions.ReadWrite), Is.EqualTo("\"ReadWrite\""));
            Assert.That(TestJson.Serialize(Permissions.ReadWrite | Permissions.Execute), Is.EqualTo("\"ReadWrite, Execute\""));
            Assert.That(TestJson.Serialize((Permissions)8), Is.EqualTo("8"));
            Assert.That(TestJson.Serialize((Permissions)9), Is.EqualTo("9"));

            Assert.That(TestJson.Deserialize<Permissions>("\"Write, Read\""), Is.EqualTo(Permissions.ReadWrite));
            Assert.That(TestJson.Deserialize<Permissions>("\"Execute\""), Is.EqualTo(Permissions.Execute));
            Assert.That(TestJson.Deserialize<Permissions>("6"), Is.EqualTo(Permissions.Write | Permissions.Execute));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Permissions>("\"Read, Delete\""));

            var colors = TestJson.Deserialize<Dictionary<string, Color>>("""{"a":"Red","b":2}""");
            Assert.That(colors["a"], Is.EqualTo(Color.Red));
            Assert.That(colors["b"], Is.EqualTo(Color.Blue));
            Assert.That(TestJson.Serialize(new Dictionary<string, Color> { ["x\"y"] = (Color)7 }), Is.EqualTo("""{"x\"y":7}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Dictionary<string, Color>>("""{"a":"red"}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"Sm":256}"""));
        }

        [Test]
        public void TestRecursiveTypes()
        {
            var json = """{"Value":1,"Children":[{"Value":2,"Children":null},{"Value":3,"Children":[]}]}""";
            var node = TestJson.Deserialize<Node>(json);

            Assert.That(node.Children, Has.Count.EqualTo(2));
            Assert.That(node.Children![0].Value, Is.EqualTo(2));
            Assert.That(TestJson.Serialize(node), Is.EqualTo(json));
        }

        [Test]
        public void TestInheritance()
        {
            var json = TestJson.Serialize(new Derived { BaseValue = 1, DerivedValue = 2 });
            Assert.That(json, Is.EqualTo("""{"BaseValue":1,"DerivedValue":2,"Name":"derived"}"""));

            var d = TestJson.Deserialize<Derived>("""{"BaseValue":3,"Name":"n","DerivedValue":4}""");
            Assert.That(d.BaseValue, Is.EqualTo(3));
            Assert.That(d.Name, Is.EqualTo("n"));
            Assert.That(d.DerivedValue, Is.EqualTo(4));
        }

        [Test]
        public void TestStructs()
        {
            var json = TestJson.Serialize(new MutableStruct { A = 1, B = "b" });
            Assert.That(json, Is.EqualTo("""{"A":1,"B":"b"}"""));

            var s = TestJson.Deserialize<MutableStruct>(json);
            Assert.That(s.A, Is.EqualTo(1));
            Assert.That(s.B, Is.EqualTo("b"));
        }

        [Test]
        public void TestNamesAndOptions()
        {
            Assert.That(TestJson.Serialize(new Keywords { @class = 1, URLValue = "u" }), Is.EqualTo("""{"class":1,"URLValue":"u"}"""));
            Assert.That(TestJson.Deserialize<Keywords>("""{"class":2}""").@class, Is.EqualTo(2));

            Assert.That(CamelJson.Serialize(new Keywords { @class = 1, URLValue = "u" }), Is.EqualTo("""{"class":1,"urlValue":"u"}"""));
            Assert.That(CamelJson.Deserialize<Keywords>("""{"URLVALUE":"x"}""").URLValue, Is.EqualTo("x"));

            // WhenWritingDefault omits null, 0, false, and default enum values
            Assert.That(CamelJson.Serialize(new Person { Default = 0 }), Is.EqualTo("""{"name":"","tags":[],"computed":"computed"}"""));
            var p = CamelJson.Deserialize<Person>("""{"NAME":"n","E-MAIL":"e","favoritecolor":"Blue"}""");
            Assert.That(p.Name, Is.EqualTo("n"));
            Assert.That(p.Email, Is.EqualTo("e"));
            Assert.That(p.FavoriteColor, Is.EqualTo(Color.Blue));
        }

        [Test]
        public void TestTypeMismatches()
        {
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>("""{"Age":"1"}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>("""{"Age":1.5}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>("""{"Name":1}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>("""{"Tags":"a"}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>("[]"));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>("{} x"));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"C":"xy"}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"By":256}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"UI":-1}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"DT":"yesterday"}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"DTO":"yesterday"}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"G":"no guid"}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"F":1e39}"""));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<AllTypes>("""{"B":"true"}"""));
        }

        [Test]
        public void TestUnregisteredType()
        {
            Assert.Throws<NotSupportedException>(() => TestJson.Deserialize<Address>("{}"));
            Assert.Throws<NotSupportedException>(() => TestJson.Deserialize<long>("1"));
        }

        [Test]
        public void TestNonFiniteNumbers()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TestJson.Serialize(new AllTypes { D = double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => TestJson.Serialize(new AllTypes { F = float.PositiveInfinity }));
        }

        [Test]
        public void TestComposition()
        {
            var w = new JsonWriter();
            w.WriteStartArray();
            TestJson.Serialize(w, new Point(1, 2));
            TestJson.Serialize(w, 3);
            w.WriteEndArray();
            Assert.That(w.ToString(), Is.EqualTo("""[{"X":1,"Y":2},3]"""));

            var r = new JsonReader(w.ToString());
            r.ReadStartArray();
            Assert.That(r.TryReadNextItem());
            Assert.That(TestJson.Deserialize<Point>(r), Is.EqualTo(new Point(1, 2)));
            Assert.That(r.TryReadNextItem());
            Assert.That(TestJson.Deserialize<int>(r), Is.EqualTo(3));
            Assert.That(r.TryReadNextItem(), Is.False);
            r.ReadEndOfInput();
        }
    }
}
