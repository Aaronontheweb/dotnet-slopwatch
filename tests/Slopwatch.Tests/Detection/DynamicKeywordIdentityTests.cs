using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Slopwatch.Baseline;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;
using Xunit;

namespace Slopwatch.Tests.Detection;

public class DynamicKeywordIdentityTests
{
    public static TheoryData<string, string> DistinctOccurrences => new()
    {
        { "constructors", "class C { static C(){dynamic x=1;} public C(){dynamic x=1;} }" },
        { "explicit methods", "interface I{void M();} interface J{void M();} class C:I,J { void I.M(){dynamic x=1;} void J.M(){dynamic x=1;} }" },
        { "explicit properties", "interface I{int P{get;}} interface J{int P{get;}} class C:I,J { int I.P{get{dynamic x=1;return 1;}} int J.P{get{dynamic x=1;return 1;}} }" },
        { "explicit indexers", "interface I{int this[int i]{get;}} interface J{int this[int i]{get;}} class C:I,J { int I.this[int i]{get{dynamic x=1;return 1;}} int J.this[int i]{get{dynamic x=1;return 1;}} }" },
        { "blocks", "class C { void M(){ {dynamic x=1;} {dynamic x=1;} } }" },
        { "filtered catches", "class C { void M(){ try{} catch(System.Exception) when(true){dynamic x=1;} catch(System.Exception){dynamic x=1;} } }" },
        { "lambda arguments", "class C { void F(System.Action a,System.Action b){} void M(){F(()=>{dynamic x=1;},()=>{dynamic x=1;});} }" },
        { "deconstructing foreach", "class C { void M((int,int)[] a){ foreach(var(x,y) in a){dynamic z=1;} foreach(var(x,y) in a){dynamic z=1;} } }" },
        { "checked operators", "class C { public static C operator +(C a,C b){dynamic x=1;return a;} public static C operator checked +(C a,C b){dynamic x=1;return a;} }" },
        { "statement ordinals", "class C { void M(){object x=1;x=(dynamic)1;x=(dynamic)1;} }" },
        { "method arity", "class C { void M<T>(){dynamic x=1;} void M<T,U>(){dynamic x=1;} }" },
        { "type arity", "class C { void M(){dynamic x=1;} } class C<T> { void M(){dynamic x=1;} }" },
        { "parameter modifiers", "class C { void N(int a){dynamic x=1;} void N(ref int a){dynamic x=1;} }" },
        { "accessors", "class C { int P {get{dynamic x=1;return 1;} set{dynamic x=1;}} }" },
        { "local functions", "class C { void M(){ {void L(){dynamic x=1;}} {void L(){dynamic x=1;}} } }" },
        { "nested wrappers", "class C { void F(System.Action a,System.Action b){} void M(){F(()=>{F(()=>{dynamic x=1;},()=>{dynamic x=1;});},()=>{});} }" },
        { "repeated namespace headers", "namespace N {using D=dynamic;} namespace N {using D=dynamic;}" },
        { "repeated partial type headers", "partial class C:System.Collections.Generic.List<dynamic>{} partial class C:System.Collections.Generic.List<dynamic>{}" }
    };

    [Theory]
    [MemberData(nameof(DistinctOccurrences))]
    public async Task OccurrencesHaveDistinctHashesAndTotalPaths(string scenario, string code)
    {
        AssertCompiles(code);
        var results = await DetectAsync(code);
        Assert.Equal(2, results.Count);
        Assert.NotEqual(Hash(results[0]), Hash(results[1]));

        var nodes = CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes()
            .OfType<IdentifierNameSyntax>().Where(n => n.Identifier.ValueText == "dynamic").ToArray();
        for (var i = 0; i < nodes.Length; i++)
        {
            // This checks the invariant itself, including previously unhandled wrappers.
            Assert.Equal(nodes[i].AncestorsAndSelf().Count(), ReadPath(results[i].CodeSnippet!).Count);
        }

        var baseline = BaselineFile.Create(new[] { results[0] }, "/test");
        Assert.False(baseline.IsBaselined(results[1], "/test"), scenario);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertedBranchCannotInheritBaseline(bool preserveSiblingCount)
    {
        var before = "class C { void M(bool b) {\nif(b){}\nif(b){\ndynamic x=1;\n}\n"
            + (preserveSiblingCount ? "if(!b){}\n" : "") + "} }";
        var after = "class C { void M(bool b) {\n"
            + (preserveSiblingCount ? "if(!b){}\n" : "if(b){}\n")
            + "if(b){\ndynamic x=1;\n}\nif(b){\ndynamic x=1;\n}\n} }";
        AssertCompiles(before);
        AssertCompiles(after);
        var baseline = BaselineFile.Create(await DetectAsync(before), "/test");
        var full = await DetectAsync(after);
        var added = Assert.Single(await DetectAsync(after, new[] { 3, 4, 5 }));

        Assert.Equal(4, added.LineNumber);
        Assert.False(baseline.IsBaselined(added, "/test"));
        Assert.Equal(full[0].CodeSnippet, added.CodeSnippet);
    }

    [Fact]
    public async Task CatchTypeParticipatesInIdentity()
    {
        const string before = "class C { void M(){try{}catch(System.ArgumentException){dynamic x=1;}} }";
        const string after = "class C { void M(){try{}catch(System.Exception){dynamic x=1;}} }";
        AssertCompiles(before);
        AssertCompiles(after);
        var baseline = BaselineFile.Create(await DetectAsync(before), "/test");
        Assert.False(baseline.IsBaselined(Assert.Single(await DetectAsync(after)), "/test"));
    }

    [Theory]
    [InlineData("class C {void M<T>(){dynamic x=1;}}", "class C {void M<T,U>(){dynamic x=1;}}")]
    [InlineData("class C<T> {void M(){dynamic x=1;}}", "class C<T,U> {void M(){dynamic x=1;}}")]
    [InlineData("class C {void N(int a){dynamic x=1;}}", "class C {void N(ref int a){dynamic x=1;}}")]
    public async Task ChangedSignatureCannotInheritBaseline(string before, string after)
    {
        AssertCompiles(before);
        AssertCompiles(after);
        var baseline = BaselineFile.Create(await DetectAsync(before), "/test");
        Assert.False(baseline.IsBaselined(Assert.Single(await DetectAsync(after)), "/test"));
    }

    [Theory]
    [InlineData("method", "class C { void M(){dynamic x=1;} $SIBLING$ }", "void N(){}")]
    [InlineData("signature", "class C { dynamic M(){return 1;} $SIBLING$ }", "void N(){}")]
    [InlineData("field", "class C { dynamic F=1; $SIBLING$ }", "int G=2;")]
    [InlineData("local function", "class C { void M(){void L(){dynamic x=1;} $SIBLING$} }", "void N(){}")]
    [InlineData("namespace", "namespace A { class C {void M(){dynamic x=1;}} } $SIBLING$", "namespace B {class D{}}")]
    public async Task NamedSiblingDoesNotChangeIdentity(string scenario, string template, string sibling)
    {
        var before = template.Replace("$SIBLING$", "");
        var after = template.Replace("$SIBLING$", sibling);
        AssertCompiles(before);
        AssertCompiles(after);
        var beforeHash = Hash(Assert.Single(await DetectAsync(before)));
        var afterHash = Hash(Assert.Single(await DetectAsync(after)));
        Assert.True(beforeHash == afterHash, $"Adding a named {scenario} sibling changed the baseline identity");
    }

    [Theory]
    [InlineData("class C { static void nameof(object x){} void M(){nameof((dynamic)1);} }")]
    [InlineData("class C { static void nameof<T>(){} void M(){nameof<dynamic>();} }")]
    public async Task RealNameofCallableDoesNotExcludeDynamic(string code)
    {
        AssertCompiles(code);
        Assert.Single(await DetectAsync(code));
    }

    private static string Hash(DetectionResult result) =>
        BaselineEntry.ComputeHash(result.RuleId, "TestFile.cs", result.CodeSnippet!);

    private static List<string> ReadPath(string snippet)
    {
        var segments = new List<string>();
        var offset = 0;
        do
        {
            var colon = snippet.IndexOf(':', offset);
            var length = int.Parse(snippet.AsSpan(offset, colon - offset));
            offset = colon + 1;
            segments.Add(snippet.Substring(offset, length));
            offset += length;
            if (snippet[offset] != '/')
                return segments;
            offset++;
        } while (offset < snippet.Length);
        throw new InvalidOperationException("Structural path has no source snippet");
    }

    private static void AssertCompiles(string code)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("identity-fixture",
            new[] { CSharpSyntaxTree.ParseText(code) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static async Task<List<DetectionResult>> DetectAsync(string code, int[]? addedLines = null)
    {
        var context = new DetectionContext("/test/TestFile.cs", "TestFile.cs", code,
            CSharpSyntaxTree.ParseText(code), false,
            addedLines is null ? null : new HashSet<int>(addedLines));
        var results = new List<DetectionResult>();
        await foreach (var result in new DynamicKeywordRule().AnalyzeAsync(context))
            results.Add(result);
        return results;
    }
}
