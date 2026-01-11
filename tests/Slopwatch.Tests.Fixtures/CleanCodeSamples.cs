using Xunit;

namespace Slopwatch.Tests.Fixtures;

/// <summary>
/// Clean code samples that should NOT trigger any Slopwatch rules
/// Demonstrates proper coding patterns and best practices
/// </summary>
public class CleanCodeSamples
{
    // ==========================================
    // CLEAN TEST PATTERNS (Should NOT trigger SW001)
    // ==========================================

    [Fact]
    public void NormalEnabledTest_ShouldNotTriggerAnyRules()
    {
        var calculator = new Calculator();
        var result = calculator.Add(2, 3);
        Assert.Equal(5, result);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 2)]
    [InlineData(-1, 1, 0)]
    [InlineData(100, 200, 300)]
    public void ParameterizedTest_WithMultipleInputs_ShouldNotTriggerAnyRules(int a, int b, int expected)
    {
        var calculator = new Calculator();
        var result = calculator.Add(a, b);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task AsyncTest_ProperlyAwaited_ShouldNotTriggerAnyRules()
    {
        var service = new AsyncService();
        var result = await service.GetDataAsync();
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    // ==========================================
    // CLEAN EXCEPTION HANDLING (Should NOT trigger SW003)
    // ==========================================

    [Fact]
    public void ProperExceptionHandling_WithSpecificCatch_ShouldNotTriggerAnyRules()
    {
        var parser = new DataParser();

        try
        {
            var result = parser.Parse("invalid-data");
            Assert.NotNull(result);
        }
        catch (FormatException ex)
        {
            // Proper handling: specific exception type, proper action
            throw new InvalidOperationException("Unable to parse data format", ex);
        }
    }

    [Fact]
    public void ExceptionWithLoggingAndRethrow_ShouldNotTriggerAnyRules()
    {
        var logger = new ConsoleLogger();
        var processor = new DataProcessor();

        try
        {
            processor.Process("test-data");
        }
        catch (Exception ex)
        {
            logger.Log($"Processing failed: {ex.Message}");
            throw; // Properly rethrows
        }
    }

    [Fact]
    public void TryPatternWithProperErrorHandling_ShouldNotTriggerAnyRules()
    {
        var validator = new InputValidator();
        var success = validator.TryValidate("test@example.com", out var result, out var error);

        if (!success)
        {
            Assert.NotNull(error);
            throw new ArgumentException(error);
        }

        Assert.NotNull(result);
    }

    [Fact]
    public void MultipleSpecificCatchBlocks_ShouldNotTriggerAnyRules()
    {
        var connector = new DatabaseConnector();

        try
        {
            connector.Connect("connection-string");
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("Database connection timeout", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new SecurityException("Authentication failed", ex);
        }
    }

    // ==========================================
    // CLEAN ASYNC PATTERNS (Should NOT trigger SW004)
    // ==========================================

    [Fact]
    public async Task ProperAsyncWithCancellationToken_ShouldNotTriggerAnyRules()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var service = new BackgroundService();

        var result = await service.ExecuteAsync(cts.Token);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TaskCompletionSourceForSynchronization_ShouldNotTriggerAnyRules()
    {
        var eventSource = new EventSource();
        var tcs = new TaskCompletionSource<string>();

        eventSource.OnDataReceived += data => tcs.SetResult(data);
        eventSource.Start();

        var result = await tcs.Task;
        Assert.Equal("expected-data", result);
    }

    [Fact]
    public void ManualResetEventForThreadSync_ShouldNotTriggerAnyRules()
    {
        var worker = new ThreadWorker();
        using var resetEvent = new ManualResetEventSlim(false);

        worker.OnCompleted += () => resetEvent.Set();
        worker.Start();

        var signaled = resetEvent.Wait(TimeSpan.FromSeconds(5));
        Assert.True(signaled);
    }

    [Fact]
    public async Task ConfigureAwaitForLibraryCode_ShouldNotTriggerAnyRules()
    {
        var repository = new DataRepository();

        var data = await repository.LoadAsync().ConfigureAwait(false);
        var processed = await repository.ProcessAsync(data).ConfigureAwait(false);

        Assert.NotNull(processed);
    }

    // ==========================================
    // CLEAN CODE (NO WARNING SUPPRESSION - Should NOT trigger SW002)
    // ==========================================

    [Fact]
    public void WellWrittenCode_NoSuppressions_ShouldNotTriggerAnyRules()
    {
        var numbers = new[] { 1, 2, 3, 4, 5 };
        var sum = numbers.Sum();
        var average = numbers.Average();

        Assert.Equal(15, sum);
        Assert.Equal(3.0, average);
    }

    [Fact]
    public void ProperNullableHandling_ShouldNotTriggerAnyRules()
    {
        string? nullableInput = GetNullableString();

        if (nullableInput is null)
        {
            throw new ArgumentNullException(nameof(nullableInput));
        }

        var length = nullableInput.Length; // Safe: null-check above
        Assert.True(length >= 0);
    }

    [Fact]
    public void PatternMatchingForNullChecks_ShouldNotTriggerAnyRules()
    {
        var result = ProcessData("test-data");

        if (result is not null)
        {
            Assert.NotEmpty(result.Value);
        }
    }

    // ==========================================
    // CLEAN INTEGRATION PATTERNS
    // ==========================================

    [Fact]
    public async Task CleanIntegrationTest_ShouldNotTriggerAnyRules()
    {
        // Arrange
        var client = new HttpClient();
        var endpoint = "https://api.example.com/health";

        // Act
        var response = await client.GetAsync(endpoint);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResourceCleanupWithUsing_ShouldNotTriggerAnyRules()
    {
        await using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream);

        await writer.WriteLineAsync("test data");
        await writer.FlushAsync();

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("test data", content);
    }

    [Fact]
    public void ValueTaskUsageForPerformance_ShouldNotTriggerAnyRules()
    {
        var cache = new FastCache();

        // ValueTask for hot path optimization
        var result1 = cache.GetOrAddAsync("key1", () => new ValueTask<string>("value1"));
        var result2 = cache.GetOrAddAsync("key1", () => new ValueTask<string>("should-not-call"));

        Assert.Equal("value1", result1.Result);
        Assert.Equal("value1", result2.Result);
    }

    // ==========================================
    // CLEAN ERROR BOUNDARY PATTERNS
    // ==========================================

    [Fact]
    public void ResultPatternInsteadOfExceptions_ShouldNotTriggerAnyRules()
    {
        var validator = new Validator();
        var result = validator.Validate("test-input");

        if (result.IsSuccess)
        {
            Assert.NotNull(result.Value);
        }
        else
        {
            Assert.NotNull(result.Error);
            throw new ValidationException(result.Error);
        }
    }

    [Fact]
    public async Task OptionMonadPattern_ShouldNotTriggerAnyRules()
    {
        var repository = new UserRepository();
        var userOption = await repository.FindByIdAsync(123);

        var userName = userOption.Match(
            some: user => user.Name,
            none: () => "Unknown User"
        );

        Assert.NotEmpty(userName);
    }

    // ==========================================
    // HELPER CLASSES
    // ==========================================

    private static string? GetNullableString() => "test";

    private static ResultData? ProcessData(string input) =>
        new ResultData { Value = input };

    private class Calculator
    {
        public int Add(int a, int b) => a + b;
    }

    private class AsyncService
    {
        public Task<List<string>> GetDataAsync() =>
            Task.FromResult(new List<string> { "data1", "data2" });
    }

    private class DataParser
    {
        public object Parse(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new FormatException("Invalid input");
            return new object();
        }
    }

    private class ConsoleLogger
    {
        public void Log(string message) => Console.WriteLine(message);
    }

    private class DataProcessor
    {
        public void Process(string data) { }
    }

    private class InputValidator
    {
        public bool TryValidate(string input, out string? result, out string? error)
        {
            if (string.IsNullOrEmpty(input))
            {
                result = null;
                error = "Input cannot be empty";
                return false;
            }

            result = input;
            error = null;
            return true;
        }
    }

    private class DatabaseConnector
    {
        public void Connect(string connectionString) { }
    }

    private class BackgroundService
    {
        public Task<ServiceResult> ExecuteAsync(CancellationToken ct) =>
            Task.FromResult(new ServiceResult { IsSuccess = true });
    }

    private class ServiceResult
    {
        public bool IsSuccess { get; set; }
    }

    private class EventSource
    {
        public event Action<string>? OnDataReceived;
        public void Start() => Task.Run(() => OnDataReceived?.Invoke("expected-data"));
    }

    private class ThreadWorker
    {
        public event Action? OnCompleted;
        public void Start() => Task.Run(() => OnCompleted?.Invoke());
    }

    private class DataRepository
    {
        public Task<object> LoadAsync() => Task.FromResult(new object());
        public Task<object> ProcessAsync(object data) => Task.FromResult(data);
    }

    private class ResultData
    {
        public string Value { get; set; } = string.Empty;
    }

    private class FastCache
    {
        private readonly Dictionary<string, string> _cache = new();

        public ValueTask<string> GetOrAddAsync(string key, Func<ValueTask<string>> factory)
        {
            if (_cache.TryGetValue(key, out var value))
                return new ValueTask<string>(value);

            var result = factory().Result;
            _cache[key] = result;
            return new ValueTask<string>(result);
        }
    }

    private class Validator
    {
        public ValidationResult Validate(string input) =>
            new ValidationResult { IsSuccess = true, Value = input };
    }

    private class ValidationResult
    {
        public bool IsSuccess { get; set; }
        public string? Value { get; set; }
        public string? Error { get; set; }
    }

    private class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }

    private class UserRepository
    {
        public Task<Option<User>> FindByIdAsync(int id) =>
            Task.FromResult(Option<User>.Some(new User { Name = "John Doe" }));
    }

    private class User
    {
        public string Name { get; set; } = string.Empty;
    }

    private class Option<T>
    {
        private readonly T? _value;
        private readonly bool _hasValue;

        private Option(T value, bool hasValue)
        {
            _value = value;
            _hasValue = hasValue;
        }

        public static Option<T> Some(T value) => new(value, true);
        public static Option<T> None() => new(default!, false);

        public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
            _hasValue ? some(_value!) : none();
    }

    private class SecurityException : Exception
    {
        public SecurityException(string message, Exception inner) : base(message, inner) { }
    }
}

/// <summary>
/// Additional clean code patterns for complex scenarios
/// </summary>
public class AdvancedCleanPatterns
{
    [Fact]
    public async Task ChannelPatternForProducerConsumer_ShouldNotTriggerAnyRules()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();

        // Producer
        var producer = Task.Run(async () =>
        {
            await channel.Writer.WriteAsync("item1");
            await channel.Writer.WriteAsync("item2");
            channel.Writer.Complete();
        });

        // Consumer
        var items = new List<string>();
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            items.Add(item);
        }

        await producer;
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task SemaphoreForConcurrencyControl_ShouldNotTriggerAnyRules()
    {
        using var semaphore = new SemaphoreSlim(2, 2); // Max 2 concurrent operations
        var tasks = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await Task.Delay(10); // Simulate work
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(5, tasks.Count);
    }

    [Fact]
    public void StructuredConcurrencyPattern_ShouldNotTriggerAnyRules()
    {
        var cts = new CancellationTokenSource();
        var tasks = Enumerable.Range(0, 3)
            .Select(i => Task.Run(() => PerformWork(i, cts.Token)))
            .ToArray();

        Task.WaitAll(tasks);
        Assert.All(tasks, t => Assert.True(t.IsCompletedSuccessfully));
    }

    private static void PerformWork(int id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Work here
    }
}
