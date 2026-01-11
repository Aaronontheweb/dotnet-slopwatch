using Xunit;
using Slopwatch.Suppression;

namespace Slopwatch.Tests.Fixtures;

/// <summary>
/// Test fixtures for SW001 - Disabled Test Detection
/// Contains both violating and clean examples
/// </summary>
public class DisabledTestSamples
{
    // SHOULD TRIGGER: SW001
    // xUnit Fact with Skip parameter
    [Fact(Skip = "This test is flaky and fails sometimes")]
    public void DisabledTest_WithSkipReason_ShouldTrigger()
    {
        Assert.True(true);
    }

    // SHOULD TRIGGER: SW001
    // xUnit Theory with empty Skip reason
    [Theory(Skip = "")]
    [InlineData(1)]
    [InlineData(2)]
    public void DisabledTheory_WithEmptySkip_ShouldTrigger(int value)
    {
        Assert.True(value > 0);
    }

    // SHOULD TRIGGER: SW001
    // Multiple test attributes, one disabled
    [Fact(Skip = "Broken after recent refactoring")]
    [Trait("Category", "Integration")]
    public void DisabledIntegrationTest_ShouldTrigger()
    {
        // This test was disabled because it started failing
        // but the root cause was never fixed
        var result = PerformComplexOperation();
        Assert.NotNull(result);
    }

#if false
    // SHOULD TRIGGER: SW001
    // Test wrapped in #if false preprocessor directive
    [Fact]
    public void TestDisabledWithPreprocessor_ShouldTrigger()
    {
        // This entire block is disabled via preprocessor
        Assert.True(false, "This test never runs");
    }

    // SHOULD TRIGGER: SW001
    // Another test in the same #if false block
    [Theory]
    [InlineData("test")]
    public void AnotherDisabledTest_ShouldTrigger(string input)
    {
        Assert.NotEmpty(input);
    }
#endif

    // SHOULD TRIGGER: SW001
    // Realistic scenario: LLM generated this test but it doesn't work
    [Fact(Skip = "Generated test doesn't match actual behavior")]
    public void GeneratedTest_ThatDoesntWork_ShouldTrigger()
    {
        var calculator = new SimpleCalculator();
        var result = calculator.Add(2, 2);
        // LLM expected 5 but actual behavior returns 4
        Assert.Equal(5, result); // This is wrong, so test was disabled
    }

    // SHOULD NOT TRIGGER
    // Properly suppressed with valid SlopwatchSuppress attribute
    [Fact(Skip = "Windows-only test - requires COM components")]
    [SlopwatchSuppress(
        "SW001",
        "This test validates Windows COM automation which requires COM components only available on Windows. Cannot run in Linux CI environment.",
        "Platform",
        issueUrl: "https://github.com/stannardlabs/dotnet-slopwatch/issues/123")]
    public void WindowsOnlyTest_WithValidSuppression_ShouldNotTrigger()
    {
        // This is a legitimate platform-specific test
        // It has proper suppression with detailed justification
        Assert.True(true);
    }

    // SHOULD NOT TRIGGER
    // Properly suppressed TDD scenario
    [Fact(Skip = "Feature not yet implemented - TDD red phase")]
    [SlopwatchSuppress(
        "SW001",
        "Test-driven development: This test defines expected behavior for the caching layer that is currently being implemented. Part of sprint 24 deliverables.",
        "TDD",
        issueUrl: "#456",
        reviewer: "tech-lead@company.com")]
    public void CachingBehavior_TddNotYetImplemented_ShouldNotTrigger()
    {
        var cache = new DistributedCache();
        cache.Set("key", "value");
        Assert.Equal("value", cache.Get("key"));
    }

    // SHOULD NOT TRIGGER
    // Normal, enabled test
    [Fact]
    public void NormalEnabledTest_ShouldNotTrigger()
    {
        Assert.True(true);
    }

    // SHOULD NOT TRIGGER
    // Another normal test with complex logic
    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(5, 7, 12)]
    [InlineData(-1, 1, 0)]
    public void AdditionTest_WithMultipleInputs_ShouldNotTrigger(int a, int b, int expected)
    {
        var calculator = new SimpleCalculator();
        var result = calculator.Add(a, b);
        Assert.Equal(expected, result);
    }

    // SHOULD NOT TRIGGER
    // External dependency test with proper suppression
    [Fact(Skip = "Requires Azure Storage Emulator")]
    [SlopwatchSuppress(
        "SW001",
        "Integration test requires Azure Storage Emulator running locally. Emulator is not available in CI environment. Test runs in local development and pre-production environments.",
        "External",
        issueUrl: "https://dev.azure.com/company/project/_workitems/edit/789")]
    public void AzureStorageTest_WithExternalDependency_ShouldNotTrigger()
    {
        // Legitimate external dependency
        Assert.True(true);
    }

    // Helper classes for realistic tests
    private class SimpleCalculator
    {
        public int Add(int a, int b) => a + b;
    }

    private class DistributedCache
    {
        public void Set(string key, string value) { }
        public string? Get(string key) => null;
    }

    private object PerformComplexOperation() => new object();
}

/// <summary>
/// Additional examples with NUnit-style attributes (for cross-framework testing)
/// Note: These won't compile without NUnit package, but demonstrate the pattern
/// </summary>
public class NUnitStyleDisabledTests
{
    // SHOULD TRIGGER: SW001
    // [Ignore] attribute (NUnit style)
    // [Ignore("Broken test that needs fixing")]
    public void NUnitIgnoredTest_ShouldTrigger()
    {
        // This pattern is common in NUnit codebases
        // but we're not including NUnit package in this fixture
    }
}
