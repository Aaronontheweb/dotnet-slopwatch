using System.Diagnostics;
using Xunit;
using Slopwatch.Suppression;

namespace Slopwatch.Tests.Fixtures;

/// <summary>
/// Test fixtures for SW003 - Empty Catch Block Detection
/// Contains both violating and clean examples
/// </summary>
public class EmptyCatchSamples
{
    // SHOULD TRIGGER: SW003 (Error severity)
    // Completely empty catch block
    public void EmptyCatchBlock_ShouldTrigger()
    {
        try
        {
            var result = RiskyOperation();
            Console.WriteLine(result);
        }
        catch (Exception)
        {
            // Empty - swallows all exceptions
        }
    }

    // SHOULD TRIGGER: SW003 (Error severity)
    // Catch block with only comments
    public void CatchWithOnlyComments_ShouldTrigger()
    {
        try
        {
            var data = LoadConfiguration();
            ProcessData(data);
        }
        catch (IOException)
        {
            // TODO: Handle this properly
            // For now, just ignore file errors
        }
    }

    // SHOULD NOT TRIGGER (logging IS handling)
    // Catch block that logs is legitimate - fire-and-forget, background jobs, graceful degradation
    public void CatchWithOnlyLogging_ShouldNotTrigger()
    {
        try
        {
            var result = PerformDatabaseOperation();
            SaveResult(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error occurred: {ex.Message}");
            // Logging IS handling - this is valid for fire-and-forget scenarios
        }
    }

    // SHOULD NOT TRIGGER (logging IS handling)
    // Using logger is valid handling - logs the failure for debugging
    public void CatchWithLogger_NoRethrow_ShouldNotTrigger()
    {
        var logger = CreateLogger();
        try
        {
            var data = FetchFromApi();
            ProcessApiData(data);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError($"API call failed: {ex}");
            // Logging is valid handling for non-critical operations
        }
    }

    // SHOULD NOT TRIGGER (has actual handling)
    // Broad exception catch with actual handling is legitimate
    public void BroadExceptionCatch_WithHandling_ShouldNotTrigger()
    {
        try
        {
            var value = ParseInput("42");
            ProcessValue(value);
        }
        catch (Exception)
        {
            // Has actual handling code - this is legitimate for top-level handlers
            Console.WriteLine("Something went wrong");
        }
    }

    // SHOULD NOT TRIGGER (has actual handling)
    // Catch without type but with actual handling
    public void CatchAll_WithHandling_ShouldNotTrigger()
    {
        try
        {
            DangerousOperation();
        }
        catch
        {
            // Has actual handling code
            Debug.WriteLine("Error suppressed");
        }
    }

    // SHOULD TRIGGER: SW003 (Error severity)
    // LLM-generated code that swallows exceptions
    public void LlmGeneratedEmptyCatch_ShouldTrigger()
    {
        try
        {
            var client = new HttpClient();
            var response = client.GetAsync("https://api.example.com/data").Result;
            var content = response.Content.ReadAsStringAsync().Result;
        }
        catch (Exception)
        {
            // LLM added try-catch but didn't implement error handling
        }
    }

    // SHOULD NOT TRIGGER (logging IS handling)
    // Multiple catch blocks, logging is valid handling
    public void MultipleCatchBlocks_OneWithOnlyLogging_ShouldNotTrigger()
    {
        try
        {
            ReadFromDatabase();
        }
        catch (TimeoutException)
        {
            // Properly handles timeout
            throw new InvalidOperationException("Database timeout - retry later");
        }
        catch (Exception ex)
        {
            // Logging IS handling - valid for graceful degradation
            Console.WriteLine($"Unexpected error: {ex}");
        }
    }

    // SHOULD NOT TRIGGER
    // Properly suppressed for optional configuration file
    [SlopwatchSuppress(
        "SW003",
        "Optional configuration file - FileNotFoundException is expected when config doesn't exist. Application uses default configuration in this case. Default values are tested in ConfigurationTests.DefaultValuesTest.",
        "External")]
    public void LoadOptionalConfig_WithValidSuppression_ShouldNotTrigger()
    {
        try
        {
            var config = File.ReadAllText("optional-config.json");
            ApplyConfiguration(config);
        }
        catch (FileNotFoundException)
        {
            // Expected - use defaults
        }
    }

    // SHOULD NOT TRIGGER
    // Properly handles exception with business logic
    public void ProperExceptionHandling_ShouldNotTrigger()
    {
        try
        {
            var result = ValidateInput("test@example.com");
            ProcessValidatedInput(result);
        }
        catch (ArgumentException ex)
        {
            // Properly handles by converting to user-friendly message
            throw new InvalidOperationException(
                "Invalid email address provided. Please check the format.",
                ex);
        }
        catch (FormatException ex)
        {
            // Another proper handler
            LogAndNotifyUser(ex.Message);
            throw;
        }
    }

    // SHOULD NOT TRIGGER
    // Catch with proper error handling and return
    public bool TryParseValue_ShouldNotTrigger(string input, out int result)
    {
        try
        {
            result = int.Parse(input);
            return true;
        }
        catch (FormatException)
        {
            // Proper handling - returns error state
            result = 0;
            return false;
        }
    }

    // SHOULD NOT TRIGGER
    // Catch that logs AND rethrows
    public void LogAndRethrow_ShouldNotTrigger()
    {
        var logger = CreateLogger();
        try
        {
            var data = LoadCriticalData();
            ProcessCriticalData(data);
        }
        catch (Exception ex)
        {
            logger.LogError($"Critical operation failed: {ex}");
            throw; // Rethrows - this is proper
        }
    }

    // SHOULD NOT TRIGGER
    // Specific exception types with proper handling
    public void SpecificExceptionHandling_ShouldNotTrigger()
    {
        try
        {
            ConnectToDatabase();
        }
        catch (SqlException ex) when (ex.Number == 1205) // Deadlock
        {
            // Specific handling for deadlock
            RetryWithBackoff();
        }
        catch (SqlException ex) when (ex.Number == -2) // Timeout
        {
            // Specific handling for timeout
            throw new TimeoutException("Database operation timed out", ex);
        }
    }

    // SHOULD NOT TRIGGER
    // Resource cleanup scenario with proper suppression
    [SlopwatchSuppress(
        "SW003",
        "Cleanup code in disposal pattern - exceptions during cleanup should not propagate to prevent masking original exceptions. Following Microsoft's IDisposable implementation guidelines for defensive cleanup.",
        "Performance",
        reviewer: "architecture-team@company.com")]
    public void CleanupCode_WithValidSuppression_ShouldNotTrigger()
    {
        try
        {
            DisposeExpensiveResource();
        }
        catch (Exception)
        {
            // Suppress cleanup errors to avoid masking primary exception
        }
    }

    // SHOULD NOT TRIGGER
    // Testing-specific scenario with proper suppression
    [Fact]
    [SlopwatchSuppress(
        "SW003",
        "Integration test validates that rate limiter correctly rejects excessive requests by throwing RateLimitExceededException. Empty catch is intentional to verify exception is thrown. Test assertion validates behavior after catch block.",
        "Testing")]
    public void RateLimiterTest_ExpectsException_ShouldNotTrigger()
    {
        var limiter = CreateRateLimiter(maxRequests: 3);
        var exceptionThrown = false;

        // Make requests up to limit
        for (int i = 0; i < 3; i++)
        {
            limiter.AllowRequest();
        }

        // Next request should throw
        try
        {
            limiter.AllowRequest();
        }
        catch (RateLimitExceededException)
        {
            exceptionThrown = true;
            // Empty catch is intentional - we're testing that exception occurs
        }

        Assert.True(exceptionThrown, "Rate limiter should throw on exceeding limit");
    }

    // SHOULD NOT TRIGGER
    // No try-catch at all
    public void NoTryCatch_ShouldNotTrigger()
    {
        var result = SafeOperation();
        ProcessResult(result);
    }

    // Helper methods and classes
    private static string RiskyOperation() => "result";
    private static object LoadConfiguration() => new object();
    private static void ProcessData(object data) { }
    private static object PerformDatabaseOperation() => new object();
    private static void SaveResult(object result) { }
    private static object FetchFromApi() => new object();
    private static void ProcessApiData(object data) { }
    private static int ParseInput(string input) => int.Parse(input);
    private static void ProcessValue(int value) { }
    private static void DangerousOperation() { }
    private static void ReadFromDatabase() { }
    private static void ApplyConfiguration(string config) { }
    private static string ValidateInput(string input) => input;
    private static void ProcessValidatedInput(string result) { }
    private static void LogAndNotifyUser(string message) { }
    private static object LoadCriticalData() => new object();
    private static void ProcessCriticalData(object data) { }
    private static void ConnectToDatabase() { }
    private static void RetryWithBackoff() { }
    private static void DisposeExpensiveResource() { }
    private static object SafeOperation() => new object();
    private static void ProcessResult(object result) { }

    private static ILogger CreateLogger() => new ConsoleLogger();

    private interface ILogger
    {
        void LogError(string message);
    }

    private class ConsoleLogger : ILogger
    {
        public void LogError(string message) => Console.WriteLine(message);
    }

    private class SqlException : Exception
    {
        public int Number { get; set; }
    }

    private class RateLimitExceededException : Exception { }

    private class RateLimiter
    {
        private int _requestCount;
        private readonly int _maxRequests;

        public RateLimiter(int maxRequests)
        {
            _maxRequests = maxRequests;
        }

        public void AllowRequest()
        {
            if (_requestCount >= _maxRequests)
                throw new RateLimitExceededException();
            _requestCount++;
        }
    }

    private static RateLimiter CreateRateLimiter(int maxRequests) => new RateLimiter(maxRequests);
}
