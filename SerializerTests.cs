using System;
using System.Collections.Generic;
using System.ComponentModel;
using NUnit.Framework;
using System.CodeDom.Compiler;

#pragma warning disable 1591
namespace MicroJson
{
    [TestFixture]
    [GeneratedCode("dummy", "dummy")]
    public class SerializerTests
    {
        public class SerializeInner
        {
            public string b2;
        }

        [Flags]
        public enum AnEnum { Test1 = 1, Test2 = 2, Test3 = 4, Test4 = 8 };

        public class Serialize
        {
            [DefaultValue(4711)]
            public int I { get; set; }
            public bool B { get; set; }
            public double D { get; set; }
            public string S;
            public List<string> LS;
            public SerializeInner Inner { get; set; }
            public Dictionary<string, string> X { get; set; }
            public AnEnum E { get; set; }
            public AnEnum E2 { get; set; }

            public Serialize()
            {
                I = 4711;
            }
        }

        public class DerivedSerialize : Dictionary<string, string>
        {
        }

        [Test]
        public void TestDeserialization()
        {
            var s = @"{ ""I"": 1,
""B"": true,
""D"": 1.0,
""S"": ""Test"",
""LS"": [""a"", ""b"", ""c""],
""Inner"": { ""b2"": ""xyz"" },
""X"": { ""a"": ""A"", ""b"": ""B"" },
""E"": ""Test2, Test1"",
""E2"": 9
}";
            JsonSerializer serializer = new JsonSerializer();

            var ser = serializer.Deserialize<Serialize>(s);
            Assert.AreEqual(1, ser.I);
            Assert.AreEqual(true, ser.B);
            Assert.AreEqual(1.0, ser.D);
            Assert.AreEqual("Test", ser.S);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ser.LS);
            Assert.IsNotNull(ser.Inner);
            Assert.AreEqual("xyz", ser.Inner.b2);
            Assert.AreEqual("A", ser.X["a"]);
            Assert.AreEqual("B", ser.X["b"]);
            Assert.AreEqual(AnEnum.Test2 | AnEnum.Test1, ser.E);
            Assert.AreEqual(AnEnum.Test1 | AnEnum.Test4, ser.E2);

            var s2 = @"{ ""LS"": ""a"" }";
            var d = serializer.Deserialize<Serialize>(s2);
            CollectionAssert.AreEqual(new[] { "a" }, d.LS);

            var s3 = @"{ ""A"": ""a"", ""B"": ""b"", ""C"": ""c"" }";
            var derived = serializer.Deserialize<DerivedSerialize>(s3);
            CollectionAssert.AreEqual(new Dictionary<string, string> { { "A", "a" }, { "B", "b" }, { "C", "c" } }, derived);
        }

        [Test]
        public void TestSerialization()
        {
            var o = new Serialize
            {
                I = 1,
                B = true,
                D = 2.3,
                S = "Test",
                LS = new List<string>(new[] { "a", "b", "c" }),
                Inner = new SerializeInner { b2 = "xyz" },
                X = new Dictionary<string,string> { { "b", "B" }, { "a", "A" } },
                E = AnEnum.Test2 | AnEnum.Test1,
                // default value for E2
            };

            var s = new JsonSerializer().Serialize(o);

            Assert.AreEqual(@"{""B"":true,""D"":2.3,""E"":""Test1, Test2"",""I"":1,""Inner"":{""b2"":""xyz""},""LS"":[""a"",""b"",""c""],""S"":""Test"",""X"":{""a"":""A"",""b"":""B""}}", s);
        }

        [Test]
        public void TestSerializerRoundTrip()
        {
            var s = @"{""B"":true,""D"":-1,""E"":""Test1, Test2"",""I"":-1,""Inner"":{""b2"":""xyz""},""LS"":[""a"",""b"",""c""],""S"":""Test"",""X"":{""a"":""A"",""b"":""B""}}";
            JsonSerializer serializer = new JsonSerializer();
            var ser = serializer.Deserialize<Serialize>(s);
            var s2 = serializer.Serialize(ser);
            Assert.AreEqual(s, s2);
            var ser2 = serializer.Deserialize<Serialize>(s2);
            var s3 = serializer.Serialize(ser2);
            Assert.AreEqual(s, s3);
        }
        
        [Test]
        public void TestDefaultValue()
        {
            var s = @"{""B"":true,""D"":-1,""E"":""Test1, Test2"",""Inner"":{""b2"":""xyz""},""LS"":[""a"",""b"",""c""],""S"":""Test"",""X"":{""a"":""A"",""b"":""B""}}";
            JsonSerializer serializer = new JsonSerializer();
            var ser = serializer.Deserialize<Serialize>(s);
            Assert.AreEqual(4711, ser.I);
            var s2 = serializer.Serialize(ser);
            Assert.AreEqual(s, s2);
            var ser2 = serializer.Deserialize<Serialize>(s2);
            Assert.AreEqual(4711, ser2.I);
            var s3 = serializer.Serialize(ser2);
            Assert.AreEqual(s, s3);
        }

        [Test]
        public void TestStrings()
        {
            var str = "\" \\ / \b \f \n \r \t \u4711 \\n \\\" \\\\";
            JsonSerializer serializer = new JsonSerializer();
            var ser = serializer.Serialize(str);
            Assert.AreEqual(@"""\"" \\ / \b \f \n \r \t " + "\u4711" + @" \\n \\\"" \\\\""", ser);
        }

        long convertToEpochBased(long ticks)
        {
            return (ticks - 621355968000000000) / 10000;
        }

        [Test]
        public void TestDateTime()
        {
            var str = @"""\/Date(0)\/""";
            JsonSerializer serializer = new JsonSerializer();
            var d = serializer.Deserialize<DateTime>(str);
            Assert.AreEqual(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToLocalTime(), d);
            // json resolution is only milliseconds
            var now = new DateTime((DateTime.UtcNow.Ticks / 10000) * 10000, DateTimeKind.Utc);
            str = string.Format(@"""\/Date({0})\/""", convertToEpochBased(now.Ticks));
            var dnow = serializer.Deserialize<DateTime>(str);
            Assert.AreEqual(now.ToLocalTime(), dnow);
            var x = new DateTime(1900, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            var str2 = string.Format(@"""\/Date({0})\/""", convertToEpochBased(x.Ticks));
            Assert.AreEqual(x.ToLocalTime(), serializer.Deserialize<DateTime>(str2));
            Assert.Throws<FormatException>(() => serializer.Deserialize<DateTime>(@"""hallo"""));
            Assert.Throws<FormatException>(() => serializer.Deserialize<DateTime>(@"""\/Date(hallo)\/"""));

            Assert.AreEqual(@"""\/Date(0)\/""", serializer.Serialize(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(str, serializer.Serialize(now));
            Assert.AreEqual(str2, serializer.Serialize(x));

            var n = new DateTime((DateTime.Now.Ticks / 10000) * 10000, DateTimeKind.Local);
            var n2 = serializer.Deserialize<DateTime>(serializer.Serialize(n));
            Assert.AreEqual(n, n2);
        }
    }
}
#pragma warning restore 1591