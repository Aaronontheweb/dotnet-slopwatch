using System.Diagnostics.CodeAnalysis;
using Slopwatch.Suppression;

namespace Slopwatch.Tests.Fixtures;

/// <summary>
/// Test fixtures for SW002 - Warning Suppression Detection
/// Contains both violating and clean examples
/// </summary>
public class WarningSuppressSamples
{
    // SHOULD TRIGGER: SW002
    // #pragma warning disable without matching restore
#pragma warning disable CS0649 // Field is never assigned
    private string _neverUsedField;

    public void MethodAfterPragma()
    {
        // The pragma disable is still active here - no restore was issued
        Console.WriteLine("This code is under warning suppression");
    }

    // SHOULD TRIGGER: SW002
    // Multiple warnings disabled without restore
#pragma warning disable CS8600, CS8602, CS8603
    public void NullableWarningsSuppressed()
    {
        string? nullable = null;
        var length = nullable.Length; // Would normally warn
        string assigned = nullable; // Would normally warn
    }

    // SHOULD TRIGGER: SW002
    // SuppressMessage attribute on method
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    public void MethodWithSuppressMessage()
    {
        // Using SuppressMessage to hide warning about method not using instance state
        var result = 42;
        Console.WriteLine(result);
    }

    // SHOULD TRIGGER: SW002
    // Multiple SuppressMessage attributes
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
    public void MultipleSuppressions()
    {
        try
        {
            var resource = new System.IO.MemoryStream();
            // Not disposing and catching all exceptions
        }
        catch (Exception)
        {
            // Swallowing exception
        }
    }

    // SHOULD TRIGGER: SW002
    // Realistic LLM-generated code that doesn't compile cleanly
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value
    public class GeneratedClass
    {
        public string Name { get; set; }
        public string Email { get; set; }

        // LLM forgot to initialize required properties
        // and suppressed the warning instead of fixing
    }

    // SHOULD TRIGGER: SW002
    // Suppressing nullable warnings globally in a method
#pragma warning disable CS8625, CS8600
    public string? ProblematicNullableHandling(string? input)
    {
        string required = input; // Nullable to non-nullable
        return null; // Returning null from nullable
    }

    // SHOULD NOT TRIGGER
    // Pragma with matching restore in same scope
    public void ProperPragmaUsage()
    {
#pragma warning disable CS0219 // Variable assigned but never used
        int temporary = 42; // Intentionally unused for this example
#pragma warning restore CS0219

        // Warning suppression is properly scoped
        Console.WriteLine("After restore");
    }

    // SHOULD NOT TRIGGER
    // Properly suppressed with SlopwatchSuppress
#pragma warning disable CS0618 // Using obsolete API
    [SlopwatchSuppress(
        "SW002",
        "Required to maintain backward compatibility with legacy v1 API until migration completes in Q3. New code should use v2 API. See migration guide in docs/migration-v1-to-v2.md.",
        "Legacy",
        issueUrl: "https://github.com/company/project/issues/2023",
        reviewer: "api-team@company.com")]
    public void LegacyApiUsage()
    {
        // Calling obsolete API intentionally for backward compatibility
        var result = ObsoleteMethod();
        Console.WriteLine(result);
    }

    // SHOULD NOT TRIGGER
    // Generated code scenario with proper suppression
    [SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    [SlopwatchSuppress(
        "SW002",
        "Auto-generated Entity Framework migration code. EF Core generates constructors that call virtual properties for navigation setup. Cannot modify generated code without breaking migrations.",
        "Generated",
        reviewer: "database-team@company.com")]
    public class EntityFrameworkGenerated
    {
        public EntityFrameworkGenerated()
        {
            // EF generated code calls virtual property
            InitializeNavigations();
        }

        protected virtual void InitializeNavigations() { }
    }

    // SHOULD NOT TRIGGER
    // Third-party library quirk with proper suppression
#pragma warning disable CS0618
    [SlopwatchSuppress(
        "SW002",
        "Third-party library GraphQL.NET v7.2.1 uses obsolete IResolverContext interface. Library maintainers have not yet updated to new interface. Upgrade tracked in dependency update plan.",
        "Library",
        issueUrl: "https://github.com/graphql-dotnet/graphql-dotnet/issues/3456")]
    public void ThirdPartyLibraryQuirk()
    {
        // Using obsolete interface from third-party library
        var resolver = CreateResolver();
        Console.WriteLine(resolver);
    }
#pragma warning restore CS0618

    // SHOULD NOT TRIGGER
    // Normal code without warning suppression
    public void CleanCode()
    {
        var value = 42;
        var message = $"The value is {value}";
        Console.WriteLine(message);
    }

    // SHOULD NOT TRIGGER
    // Properly scoped pragma for platform-specific code
    public void PlatformSpecificCode()
    {
#pragma warning disable CA1416 // Platform compatibility
        if (OperatingSystem.IsWindows())
        {
            // Windows-specific code
            var windowsVersion = Environment.OSVersion;
        }
#pragma warning restore CA1416
    }

    // SHOULD NOT TRIGGER
    // Interop scenario with proper suppression
    [SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage")]
    [SlopwatchSuppress(
        "SW002",
        "P/Invoke declaration for Windows kernel32.dll ReadProcessMemory function. Security reviewed by security team. Unmanaged code security required for performance-critical memory operations in diagnostic tools.",
        "Interop",
        reviewer: "security-team@company.com",
        issueUrl: "#security-review-789")]
    public class NativeInterop
    {
        // [DllImport("kernel32.dll")]
        // private static extern bool ReadProcessMemory(...);
    }

    // Helper methods
    private static string ObsoleteMethod() => "legacy result";
    private static object CreateResolver() => new object();
}

/// <summary>
/// Class demonstrating proper warning management
/// </summary>
public class CleanWarningManagement
{
    // SHOULD NOT TRIGGER
    // No warning suppressions at all
    public void WellWrittenCode()
    {
        var numbers = new List<int> { 1, 2, 3, 4, 5 };
        var sum = numbers.Sum();
        Assert.Equal(15, sum);
    }

    // SHOULD NOT TRIGGER
    // Using nullable reference types correctly without suppression
    public string ProcessInput(string? input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return input.ToUpper();
    }

    private static class Assert
    {
        public static void Equal<T>(T expected, T actual) { }
    }
}
