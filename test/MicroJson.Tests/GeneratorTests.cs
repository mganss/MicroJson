#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using MicroJson.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace MicroJson.Tests
{
    /// <summary>
    /// Runs the source generator in-process to test its diagnostics and the compilability of its output.
    /// </summary>
    [TestFixture]
    public class GeneratorTests
    {
        static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            // the test assembly itself would conflict with the test sources compiled by TestSerializationTestsSourceCompiles
            .Where(p => !string.Equals(Path.GetFileName(p), "MicroJson.Tests.dll", StringComparison.OrdinalIgnoreCase))
            .Append(typeof(JsonReader).Assembly.Location)
            .Append(typeof(Assert).Assembly.Location)
            .Distinct()
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

        static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

        record Result(ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompilationDiagnostics, string[] Sources);

        static Result Run(params string[] sources)
        {
            var compilation = CSharpCompilation.Create("Test",
                sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, ParseOptions, path: $"File{i}.cs")),
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

            GeneratorDriver driver = CSharpGeneratorDriver.Create([new MicroJsonGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

            var runResult = driver.GetRunResult();
            var generated = runResult.GeneratedTrees.Select(t => t.ToString()).ToArray();
            var compilationDiagnostics = output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToImmutableArray();

            return new Result(generatorDiagnostics, compilationDiagnostics, generated);
        }

        static string[] Ids(Result result) => result.GeneratorDiagnostics.Select(d => d.Id).ToArray();

        [Test]
        public void TestValidContextCompiles()
        {
            var result = Run("""
                using System.Collections.Generic;
                using MicroJson;
                namespace N;
                public record Item(string Name, int Count);
                public class Order { public List<Item> Items { get; set; } = new(); public Dictionary<string, decimal?> Prices { get; init; } = new(); }
                [MicroJsonSerializable(typeof(Order))]
                [MicroJsonSerializable(typeof(Item[]))]
                public partial class Json { }
                """);

            Assert.That(result.GeneratorDiagnostics, Is.Empty);
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
            Assert.That(result.Sources, Has.Length.EqualTo(1));
            Assert.That(result.Sources[0], Does.Contain("public static string Serialize(global::N.Order value)"));
            Assert.That(result.Sources[0], Does.Contain("public static string Serialize(global::N.Item[] value)"));
        }

        [Test]
        public void TestSerializationTestsSourceCompiles()
        {
            // the same input as the end-to-end tests in SerializationTests.cs, but with the generator running in-process
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SerializationTests.cs"));
            var result = Run(source);

            Assert.That(result.GeneratorDiagnostics, Is.Empty);
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
            Assert.That(result.Sources, Has.Length.EqualTo(4));
        }

        [Test]
        public void TestEquatableArray()
        {
            var a = new EquatableArray<string>(["a", "b"]);
            var b = new EquatableArray<string>(["a", "b"]);
            var c = new EquatableArray<string>(["a"]);

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals(c), Is.False);
            Assert.That(a.Equals((object)"a"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(default(EquatableArray<string>).Equals(EquatableArray<string>.Empty), Is.True);
            Assert.That(((System.Collections.IEnumerable)a).Cast<string>(), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void TestGlobalNamespaceAndSeveralDeclarations()
        {
            var result = Run(
                """
                [MicroJson.MicroJsonSerializable(typeof(int))]
                partial class Json { }
                """,
                """
                [MicroJson.MicroJsonSerializable(typeof(string))]
                partial class Json { }
                """);

            Assert.That(result.GeneratorDiagnostics, Is.Empty);
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
            Assert.That(result.Sources, Has.Length.EqualTo(1));
            Assert.That(result.Sources[0], Does.Not.Contain("namespace"));
            Assert.That(result.Sources[0], Does.Contain("Serialize(int value)"));
            Assert.That(result.Sources[0], Does.Contain("Serialize(string value)"));
        }

        [Test]
        public void TestNotPartial()
        {
            var result = Run("""
                [MicroJson.MicroJsonSerializable(typeof(int))]
                class Json { }
                """);

            Assert.That(Ids(result), Is.EqualTo(new[] { "MJ0001" }));
            Assert.That(result.Sources, Is.Empty);

            result = Run("""
                class Outer
                {
                    [MicroJson.MicroJsonSerializable(typeof(int))]
                    partial class Json { }
                }
                """);

            Assert.That(Ids(result), Is.EqualTo(new[] { "MJ0001" }));
        }

        [Test]
        public void TestGenericContext()
        {
            var result = Run("""
                [MicroJson.MicroJsonSerializable(typeof(int))]
                partial class Json<T> { }
                """);

            Assert.That(Ids(result), Is.EqualTo(new[] { "MJ0005" }));
        }

        [Test]
        public void TestUnsupportedRootTypes()
        {
            var result = Run("""
                using System;
                using System.Collections.Generic;
                using MicroJson;
                [MicroJsonSerializable(typeof(HashSet<int>))]
                [MicroJsonSerializable(typeof(Dictionary<int, string>))]
                [MicroJsonSerializable(typeof(int[,]))]
                [MicroJsonSerializable(typeof(List<>))]
                [MicroJsonSerializable(typeof(Action))]
                [MicroJsonSerializable(typeof(Version))]
                [MicroJsonSerializable(typeof(List<Action>))]
                [MicroJsonSerializable(typeof(IComparable))]
                [MicroJsonSerializable(typeof(MyList))]
                [MicroJsonSerializable(typeof(int))]
                partial class Json { }
                public class MyList : List<int> { }
                """);

            Assert.That(Ids(result), Is.EqualTo(Enumerable.Repeat("MJ0002", 9).ToArray()));
            Assert.That(result.GeneratorDiagnostics.Select(d => d.GetMessage()), Has.Some.Contains("dictionary keys must be strings"));
            Assert.That(result.GeneratorDiagnostics.Select(d => d.GetMessage()), Has.Some.Contains("multi-dimensional"));
            Assert.That(result.GeneratorDiagnostics.Select(d => d.GetMessage()), Has.Some.Contains("only arrays"));
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
        }

        [Test]
        public void TestUnsupportedMembers()
        {
            var result = Run("""
                using System;
                using MicroJson;
                public class C
                {
                    public int Ok { get; set; }
                    public Action? Callback { get; set; }
                    public System.Threading.CancellationToken Token;
                    private Hidden? H { get; set; }
                    private class Hidden { }
                }
                [MicroJsonSerializable(typeof(C))]
                partial class Json { }
                """);

            Assert.That(Ids(result), Is.EqualTo(new[] { "MJ0003", "MJ0003" }));
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
            Assert.That(result.Sources[0], Does.Contain("\"Ok\""));
            Assert.That(result.Sources[0], Does.Not.Contain("Callback"));
        }

        [Test]
        public void TestNotConstructible()
        {
            var result = Run("""
                using MicroJson;
                public abstract class A { public int X { get; set; } }
                public class B { public B(int x) { } public B(string y) { } public int X { get; set; } }
                public class C { public C(int other) { } public int X { get; set; } }
                public class D { private D() { } public int X { get; set; } }
                [MicroJsonSerializable(typeof(A))]
                [MicroJsonSerializable(typeof(B))]
                [MicroJsonSerializable(typeof(C))]
                [MicroJsonSerializable(typeof(D))]
                partial class Json { }
                """);

            Assert.That(Ids(result), Is.EqualTo(new[] { "MJ0004", "MJ0004", "MJ0004", "MJ0004" }));
            var messages = result.GeneratorDiagnostics.Select(d => d.GetMessage()).ToArray();
            Assert.That(messages, Has.Some.Contains("abstract"));
            Assert.That(messages, Has.Some.Contains("several public constructors"));
            Assert.That(messages, Has.Some.Contains("'other'"));
            Assert.That(messages, Has.Some.Contains("no public constructor"));
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
            Assert.That(result.Sources[0], Does.Contain("NotSupportedException"));
        }

        [Test]
        public void TestInvalidDerivedTypes()
        {
            var result = Run("""
                using System.Collections.Generic;
                using MicroJson;
                public class Other { }
                public abstract class AbstractDerived : Base { }
                public class Good : Base { }
                public class Good2 : Base { }
                public interface IFoo { }
                [MicroJsonDerivedType(typeof(Other), "other")]
                [MicroJsonDerivedType(typeof(AbstractDerived), "abstract")]
                [MicroJsonDerivedType(typeof(Base), "base")]
                [MicroJsonDerivedType(typeof(Good), "good")]
                [MicroJsonDerivedType(typeof(Good), "good-again")]
                [MicroJsonDerivedType(typeof(Good2), "good")]
                [MicroJsonDerivedType(typeof(IFoo), "interface")]
                public class Base { }
                [MicroJsonDerivedType(typeof(ListShape), "list")]
                public interface IShape { }
                public class ListShape : List<int>, IShape { }
                public interface IPlain { }
                [MicroJsonSerializable(typeof(Base))]
                [MicroJsonSerializable(typeof(IShape))]
                [MicroJsonSerializable(typeof(IPlain))]
                partial class Json { }
                """);

            Assert.That(Ids(result), Is.EquivalentTo(new[] { "MJ0007", "MJ0007", "MJ0007", "MJ0007", "MJ0007", "MJ0007", "MJ0007", "MJ0002" }));
            var messages = result.GeneratorDiagnostics.Where(d => d.Id == "MJ0007").Select(d => d.GetMessage()).ToArray();
            Assert.That(messages[0], Does.Contain("'Other'").And.Contain("does not derive from or implement 'Base'"));
            Assert.That(messages[1], Does.Contain("'AbstractDerived'").And.Contain("non-abstract class or struct"));
            Assert.That(messages[2], Does.Contain("'Base'").And.Contain("does not derive"));
            Assert.That(messages[3], Does.Contain("registered more than once"));
            Assert.That(messages[4], Does.Contain("discriminator 'good' is already used"));
            Assert.That(messages[5], Does.Contain("'IFoo'").And.Contain("non-abstract class or struct"));
            Assert.That(messages[6], Does.Contain("'ListShape'").And.Contain("only arrays"));
            Assert.That(result.GeneratorDiagnostics.Single(d => d.Id == "MJ0002").GetMessage(), Does.Contain("'IPlain'"));
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
        }

        [Test]
        public void TestDuplicateNames()
        {
            var result = Run("""
                using MicroJson;
                public class C
                {
                    public int A { get; set; }
                    [MicroJsonPropertyName("A")] public int B { get; set; }
                    public int a { get; set; }
                }
                [MicroJsonOptions(PropertyNameCaseInsensitive = true)]
                [MicroJsonSerializable(typeof(C))]
                partial class Json { }
                """);

            Assert.That(Ids(result), Is.EqualTo(new[] { "MJ0006", "MJ0006" }));
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
        }

        [Test]
        public void TestRecordStructAndStaticContainers()
        {
            var result = Run("""
                using MicroJson;
                public enum Empty { }
                public readonly record struct P(int X, Empty E = default);
                static partial class Outer
                {
                    partial record struct Inner
                    {
                        [MicroJsonSerializable(typeof(P))]
                        static partial class Json { }
                    }
                }
                """);

            Assert.That(result.GeneratorDiagnostics, Is.Empty);
            Assert.That(result.CompilationDiagnostics, Is.Empty, string.Join(Environment.NewLine, result.CompilationDiagnostics));
        }

        [Test]
        public void TestCamelCase()
        {
            Assert.That(ContextGenerator.CamelCase(""), Is.EqualTo(""));
            Assert.That(ContextGenerator.CamelCase("name"), Is.EqualTo("name"));
            Assert.That(ContextGenerator.CamelCase("Name"), Is.EqualTo("name"));
            Assert.That(ContextGenerator.CamelCase("URLValue"), Is.EqualTo("urlValue"));
            Assert.That(ContextGenerator.CamelCase("URL"), Is.EqualTo("url"));
            Assert.That(ContextGenerator.CamelCase("A"), Is.EqualTo("a"));
            Assert.That(ContextGenerator.CamelCase("AB C"), Is.EqualTo("ab C"));
        }

        [Test]
        public void TestIncrementalCaching()
        {
            var source = """
                [MicroJson.MicroJsonSerializable(typeof(int))]
                partial class Json { }
                """;
            var compilation = CSharpCompilation.Create("Test", [CSharpSyntaxTree.ParseText(source, ParseOptions)], References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create([new MicroJsonGenerator().AsSourceGenerator()], parseOptions: ParseOptions,
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
            driver = driver.RunGenerators(compilation);

            // an unrelated change must not regenerate the output
            compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Unrelated { }", ParseOptions));
            driver = driver.RunGenerators(compilation);

            var reasons = driver.GetRunResult().Results[0].TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs).Select(o => o.Reason).ToList();
            Assert.That(reasons, Is.Not.Empty);
            Assert.That(reasons, Is.All.Matches<IncrementalStepRunReason>(r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        }
    }
}
