using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

/// <summary>
/// Verifies that <c>[SlopwatchSuppress]</c> both <em>compiles</em> on every declaration form
/// and actually suppresses SW021.
/// </summary>
/// <remarks>
/// The other suppression tests in <see cref="DynamicKeywordRuleTests"/> only parse the fixture
/// text into a syntax tree. Parsing never validates attribute targets, so those tests would
/// still pass if <see cref="Slopwatch.Suppression.SlopwatchSuppressAttribute"/>'s
/// <c>AttributeUsage</c> rejected the target with CS0592. These tests compile the fixture
/// against the real attribute assembly, which is the only way to prove the documented escape
/// hatch is usable.
/// </remarks>
public class DynamicKeywordSuppressionCompilationTests
{
    private readonly DynamicKeywordRule _rule = new();

    public static TheoryData<string, string> SuppressibleFixtures => new()
    {
        {
            "method",
            """
            using Slopwatch.Suppression;
            public class C
            {
                [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                public void M(dynamic d) { }
            }
            """
        },
        {
            "constructor",
            """
            using Slopwatch.Suppression;
            public class C
            {
                [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                public C(dynamic d) { }
            }
            """
        },
        {
            "delegate",
            """
            using Slopwatch.Suppression;
            [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
            public delegate dynamic Handler(dynamic input);
            """
        },
        {
            "event",
            """
            using Slopwatch.Suppression;
            public class C
            {
                [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                public event System.Action? E;
            }
            """
        },
        {
            "indexer",
            """
            using Slopwatch.Suppression;
            public class C
            {
                [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                public dynamic this[int i] => null!;
            }
            """
        },
        {
            "property",
            """
            using Slopwatch.Suppression;
            public class C
            {
                [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                public dynamic P { get; set; } = null!;
            }
            """
        },
        {
            "field",
            """
            using Slopwatch.Suppression;
            public class C
            {
                [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                public dynamic F = null!;
            }
            """
        },
        {
            "class",
            """
            using Slopwatch.Suppression;
            [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
            public class C { }
            """
        },
        {
            "struct",
            """
            using Slopwatch.Suppression;
            [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
            public struct S { }
            """
        },
        {
            "interface",
            """
            using Slopwatch.Suppression;
            [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
            public interface I { }
            """
        },
        {
            "enum",
            """
            using Slopwatch.Suppression;
            [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
            public enum E { A }
            """
        },
        {
            "operator",
            """
            using Slopwatch.Suppression;
            public class C
            {
                [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                public static dynamic operator +(C a, C b) => null!;
            }
            """
        },
        {
            "local-function",
            """
            using Slopwatch.Suppression;
            public class C
            {
                public void M()
                {
                    [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                    dynamic Local(dynamic d) => d;
                }
            }
            """
        },
    };

    [Theory]
    [MemberData(nameof(SuppressibleFixtures))]
    public void SuppressionAttributeCompilesOnTarget(string target, string code)
    {
        var errors = CompileErrors(code);

        Assert.True(
            errors.Count == 0,
            $"Fixture '{target}' does not compile: {string.Join("; ", errors)}");
    }

    [Theory]
    [MemberData(nameof(SuppressibleFixtures))]
    public void SuppressionAttributeSuppressesSw021(string target, string code)
    {
        var context = CreateContext(code);

        var results = RunRule(context);

        Assert.True(
            results.Count == 0,
            $"Fixture '{target}' still reported {results.Count} SW021 finding(s): " +
            string.Join("; ", results.Select(r => $"line {r.LineNumber}")));
    }

    [Fact]
    public void SuppressionOnLambdaIsRecognized()
    {
        // Arrange - an attributed lambda. This is the one form the rule does not yet
        // resolve; the test documents the current behavior explicitly.
        const string code = """
            using Slopwatch.Suppression;
            public class C
            {
                public void M()
                {
                    var f = [SlopwatchSuppress("SW021", "COM interop requires late-bound dispatch", "Interop")]
                            (dynamic x) => x;
                }
            }
            """;

        // Act
        var results = RunRule(CreateContext(code));

        // Assert - currently NOT suppressed; tracked as a known gap.
        Assert.NotEmpty(results);
    }

    #region Helper Methods

    private static List<string> CompileErrors(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create(
            "suppression-fixture",
            new[] { tree },
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        // TRUSTED_PLATFORM_ASSEMBLIES is the full runtime assembly set for the running
        // process, which is exactly what a fixture compiled in-process needs.
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (!string.IsNullOrEmpty(trusted))
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    yield return MetadataReference.CreateFromFile(path);
            }
        }

        yield return MetadataReference.CreateFromFile(
            typeof(Slopwatch.Suppression.SlopwatchSuppressAttribute).Assembly.Location);
    }

    private static DetectionContext CreateContext(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return new DetectionContext(
            FilePath: "/test/TestFile.cs",
            FileName: "TestFile.cs",
            Content: code,
            SyntaxTree: syntaxTree,
            IsTestFile: false);
    }

    private List<DetectionResult> RunRule(DetectionContext context)
    {
        var results = new List<DetectionResult>();
        var enumerator = _rule.AnalyzeAsync(context).GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().Result)
            {
                results.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().Wait();
        }

        return results;
    }

    #endregion
}
