using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace MicroJson
{
    [TestFixture]
    public class ParserTests
    {
        JsonParser Parser;

        [SetUp]
        public void Setup()
        {
#if DEBUG
            Parser = new JsonParser { Logger = new ConsoleLogger(), CollectLineInfo = true };
#else
            Parser = new JsonParser();
#endif
        }

        [Test]
        public void TestInvalidInput()
        {
            Assert.Throws<ParserException>(() => Parser.Parse(null));
            Assert.Throws<ParserException>(() => Parser.Parse(""));
            Assert.Throws<ParserException>(() => Parser.Parse("  \t   \n   "));
        }

        [Test]
        public void TestBool()
        {
            Assert.AreEqual(true, Parser.Parse("true  "));
            Assert.AreEqual(false, Parser.Parse("false"));
            Assert.Throws<ParserException>(() => Parser.Parse("hallo"));
            Assert.Throws<ParserException>(() => Parser.Parse("True"));
        }

        [Test]
        public void TestNumbers()
        {
            Assert.AreEqual(4711, Parser.Parse("4711"));
            Assert.AreEqual(4711.0m, Parser.Parse("4711.0"));
            Assert.AreEqual(0.1m, Parser.Parse("0.1"));
            Assert.AreEqual(-0.1m, Parser.Parse("-0.1"));
            Assert.AreEqual(-1, Parser.Parse("-1"));
            Assert.AreEqual(-1.2e-3m, Parser.Parse("-1.2e-3"));
            Assert.AreEqual(0, Parser.Parse("0"));
            Assert.Throws<ParserException>(() => Parser.Parse(".1"));
            Assert.Throws<ParserException>(() => Parser.Parse("0."));
            Assert.Throws<ParserException>(() => Parser.Parse("1.2.3"));
            Assert.Throws<ParserException>(() => Parser.Parse("+1"));
            Assert.Throws<ParserException>(() => Parser.Parse("0.e3"));
            Assert.Throws<ParserException>(() => Parser.Parse("0.2e"));
            Assert.Throws<ParserException>(() => Parser.Parse("+1"));
        }

        [Test]
        public void TestNull()
        {
            Assert.AreEqual(null, Parser.Parse("null"));
            Assert.Throws<ParserException>(() => Parser.Parse("Null"));
        }

        [Test]
        public void TestString()
        {
            Assert.AreEqual("", Parser.Parse(@""""""));
            Assert.AreEqual("xyz", Parser.Parse(@"""xyz"""));
            Assert.AreEqual(@"a ""b"" c", Parser.Parse(@"""a \""b\"" c"""));
            Assert.AreEqual("\"\\/\b\f\n\r\t", Parser.Parse(@"""\""\\\/\b\f\n\r\t"""));
            Assert.AreEqual("\u1234\u5678\uabcd", Parser.Parse(@"""\u1234\u5678\uabcd"""));
            Assert.Throws<ParserException>(() => Parser.Parse(@""""));
            Assert.Throws<ParserException>(() => Parser.Parse(@"""abc"));
            Assert.Throws<ParserException>(() => Parser.Parse("\"\u001f\""));
            Assert.Throws<ParserException>(() => Parser.Parse(@"""\x"""));
            Assert.Throws<ParserException>(() => Parser.Parse(@"""\u123"""));
            Assert.Throws<ParserException>(() => Parser.Parse(@"\\\\\"));
        }

        [Test]
        public void TestList()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (IEnumerable)Parser.Parse("[ 1, 2 , 3 ]"));
            CollectionAssert.AreEqual(new object[] { "1", 2.1m, true }, (IEnumerable)Parser.Parse(@"[""1"", 2.1, true]"));
            CollectionAssert.IsEmpty((IEnumerable)Parser.Parse("[]"));
            Assert.Throws<ParserException>(() => Parser.Parse("["));
            Assert.Throws<ParserException>(() => Parser.Parse("[1 2]"));
            Assert.Throws<ParserException>(() => Parser.Parse("[1,2,]"));
            CollectionAssert.AreEqual(new object[] { 1, 2, new[] { 3, 4, 5 }, 6 }, (IEnumerable)Parser.Parse("[1,2,[3,4,5],6]"));
            CollectionAssert.AreEqual(new[] { new object[] { } }, (IEnumerable)Parser.Parse("[[]]"));
        }

        [Test]
        public void TestDictionary()
        {
            var dict = new Dictionary<string, object> { { "a", 1 },
                { "b", "b" },
                { "c", 1.2m },
                { "d", true } 
            };
            CollectionAssert.AreEqual(dict, (IEnumerable)Parser.Parse(@"{""a"":1,""b"" :""b"" ,""c"": 1.2 , ""d"" : true}"));
            CollectionAssert.IsEmpty((IEnumerable)Parser.Parse("{ }"));
            Assert.Throws<ParserException>(() => Parser.Parse("{"));
            Assert.Throws<ParserException>(() => Parser.Parse(@"{""a"":1,}"));
            Assert.Throws<ParserException>(() => Parser.Parse(@"{""a"":1 ""b"": 2}"));
            Assert.Throws<ParserException>(() => Parser.Parse("{a:1}"));
            dict = new Dictionary<string,object> {
                { "a", 1 },
                { "b", 2 },
                { "c", new Dictionary<string, object> {
                        { "d", 3 },
                        { "e", 4 },
                        { "f", 5 },
                    }
                },
                { "g", 6 },
            };
            CollectionAssert.AreEqual(dict, (IEnumerable)Parser.Parse(@"{
""a"": 1,
""b"": 2,
""c"": {
    ""d"": 3,
    ""e"": 4,
    ""f"": 5
},
""g"": 6
}"));
        }

        [Test]
        public void TestComplex()
        {
            var o = Parser.Parse(File.ReadAllText("cal.txt"));
            Assert.NotNull(o);
        }
    }
}
