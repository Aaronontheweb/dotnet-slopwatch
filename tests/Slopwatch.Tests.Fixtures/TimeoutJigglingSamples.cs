using System.Threading;
using Xunit;
using Slopwatch.Suppression;

namespace Slopwatch.Tests.Fixtures;

/// <summary>
/// Test fixtures for SW004 - Timeout Jiggling Detection
/// Contains both violating and clean examples
/// NOTE: This rule only applies to test files (IsTestFile = true)
/// </summary>
public class TimeoutJigglingSamples
{
    // SHOULD TRIGGER: SW004
    // Task.Delay in test method
    [Fact]
    public async Task TestWithTaskDelay_ShouldTrigger()
    {
        var service = new BackgroundService();
        service.Start();

        // Bad: Using arbitrary delay hoping operation completes
        await Task.Delay(1000);

        Assert.True(service.IsRunning);
    }

    // SHOULD TRIGGER: SW004
    // Thread.Sleep in test
    [Fact]
    public void TestWithThreadSleep_ShouldTrigger()
    {
        var processor = new DataProcessor();
        processor.ProcessAsync();

        // Bad: Sleeping instead of proper synchronization
        Thread.Sleep(500);

        Assert.True(processor.IsComplete);
    }

    // SHOULD TRIGGER: SW004
    // Multiple delays in same test
    [Fact]
    public async Task TestWithMultipleDelays_ShouldTrigger()
    {
        var queue = new MessageQueue();

        queue.Enqueue("message1");
        await Task.Delay(100); // First delay

        queue.Enqueue("message2");
        await Task.Delay(100); // Second delay

        Assert.Equal(2, queue.ProcessedCount);
    }

    // SHOULD TRIGGER: SW004
    // SpinWait usage in test
    [Fact]
    public void TestWithSpinWait_ShouldTrigger()
    {
        var worker = new BackgroundWorker();
        worker.Start();

        // Bad: Busy-waiting with SpinWait
        var spinWait = new SpinWait();
        while (!worker.IsComplete)
        {
            spinWait.SpinOnce();
        }

        Assert.True(worker.IsComplete);
    }

    // SHOULD TRIGGER: SW004
    // SpinWait.SpinUntil usage
    [Fact]
    public void TestWithSpinUntil_ShouldTrigger()
    {
        var cache = new CacheService();
        cache.Initialize();

        // Bad: Using SpinWait.SpinUntil instead of proper async
        SpinWait.SpinUntil(() => cache.IsReady, TimeSpan.FromSeconds(5));

        Assert.True(cache.IsReady);
    }

    // SHOULD TRIGGER: SW004
    // LLM-generated test with delay
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task LlmGeneratedTest_WithDelay_ShouldTrigger(int delayMs)
    {
        var service = new AsyncService();
        var task = service.ExecuteAsync();

        // LLM didn't know how to properly await, so it added a delay
        await Task.Delay(delayMs);

        Assert.True(task.IsCompleted);
    }

    // SHOULD TRIGGER: SW004
    // Flaky test hidden with delay
    [Fact]
    public async Task FlakyTest_HiddenWithDelay_ShouldTrigger()
    {
        var eventAggregator = new EventAggregator();
        eventAggregator.Publish("test-event");

        // Test was flaky, so someone added delay instead of fixing race condition
        await Task.Delay(250);

        Assert.Equal(1, eventAggregator.SubscriberNotifiedCount);
    }

    // SHOULD TRIGGER: SW004
    // Integration test with arbitrary timeout
    [Fact]
    public async Task IntegrationTest_WithArbitraryTimeout_ShouldTrigger()
    {
        var database = new DatabaseConnection();
        await database.ConnectAsync();

        // Bad: Arbitrary delay hoping connection is ready
        await Task.Delay(2000);

        var result = await database.ExecuteQueryAsync("SELECT 1");
        Assert.NotNull(result);
    }

    // SHOULD NOT TRIGGER
    // Rate limiter test with proper suppression
    [Fact]
    [SlopwatchSuppress(
        "SW004",
        "Testing rate limiter behavior requires actual time delays to validate throttling mechanisms work correctly. Rate limiter uses time-based windows and must be tested with real delays. Alternative testing approaches using fake time providers were evaluated but insufficient for production validation.",
        "Testing",
        issueUrl: "https://github.com/company/project/issues/rate-limiter-testing",
        reviewer: "qa-team@company.com")]
    public async Task RateLimiterTest_WithValidSuppression_ShouldNotTrigger()
    {
        var limiter = new RateLimiter(maxRequests: 3, windowSeconds: 1);

        // Make 3 requests
        for (int i = 0; i < 3; i++)
        {
            Assert.True(limiter.TryAcquire());
        }

        // 4th should fail
        Assert.False(limiter.TryAcquire());

        // Wait for window to reset - this is legitimate for rate limiter testing
        await Task.Delay(1100);

        // Should succeed after window reset
        Assert.True(limiter.TryAcquire());
    }

    // SHOULD NOT TRIGGER
    // Debounce test with proper suppression
    [Fact]
    [SlopwatchSuppress(
        "SW004",
        "Testing debounce behavior requires time delays to verify that rapid events are correctly collapsed into single action after quiet period. Debouncing is time-dependent by definition. Using real time ensures production behavior is validated.",
        "Testing",
        reviewer: "frontend-team@company.com")]
    public async Task DebounceTest_WithValidSuppression_ShouldNotTrigger()
    {
        var debouncer = new Debouncer<string>(delayMs: 200);
        var results = new List<string>();

        // Rapid events that should be debounced
        debouncer.Debounce("event1", results.Add);
        await Task.Delay(50);
        debouncer.Debounce("event2", results.Add);
        await Task.Delay(50);
        debouncer.Debounce("event3", results.Add);

        // Wait for debounce period
        await Task.Delay(250);

        // Only last event should have triggered
        Assert.Single(results);
        Assert.Equal("event3", results[0]);
    }

    // SHOULD NOT TRIGGER
    // Timeout behavior test with proper suppression
    [Fact]
    [SlopwatchSuppress(
        "SW004",
        "Integration test validates that HTTP client timeout configuration works correctly. Must use actual delays to verify timeout mechanisms trigger at expected intervals. Mock time providers cannot validate real timeout behavior in HttpClient.",
        "Testing",
        issueUrl: "#timeout-validation-tests")]
    public async Task TimeoutBehaviorTest_WithValidSuppression_ShouldNotTrigger()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(100) };
        var slowEndpoint = CreateSlowEndpoint(delayMs: 200);

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await client.GetAsync(slowEndpoint);
        });

        // Verify timeout triggered at correct time
        await Task.Delay(50); // Small delay for cleanup
    }

    // SHOULD NOT TRIGGER
    // Proper async test with TaskCompletionSource
    [Fact]
    public async Task ProperAsyncTest_WithTaskCompletionSource_ShouldNotTrigger()
    {
        var service = new EventDrivenService();
        var tcs = new TaskCompletionSource<bool>();

        service.OnComplete += () => tcs.SetResult(true);
        service.Start();

        // Proper: Using TaskCompletionSource for synchronization
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Equal(tcs.Task, completed);
        Assert.True(await tcs.Task);
    }

    // SHOULD NOT TRIGGER
    // Test using ManualResetEvent for synchronization
    [Fact]
    public void ProperTest_WithManualResetEvent_ShouldNotTrigger()
    {
        var service = new ThreadedService();
        var resetEvent = new ManualResetEventSlim(false);

        service.OnReady += () => resetEvent.Set();
        service.Initialize();

        // Proper: Using synchronization primitive
        var signaled = resetEvent.Wait(TimeSpan.FromSeconds(5));
        Assert.True(signaled);
        Assert.True(service.IsReady);
    }

    // SHOULD NOT TRIGGER
    // Test with CancellationToken timeout
    [Fact]
    public async Task ProperTest_WithCancellationToken_ShouldNotTrigger()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new LongRunningService();

        var result = await service.ExecuteAsync(cts.Token);

        Assert.True(result.Success);
    }

    // SHOULD NOT TRIGGER
    // Test without any delays
    [Fact]
    public async Task NormalTest_NoDelays_ShouldNotTrigger()
    {
        var calculator = new Calculator();
        var result = await calculator.AddAsync(2, 3);
        Assert.Equal(5, result);
    }

    // SHOULD NOT TRIGGER
    // Properly awaiting async operations
    [Fact]
    public async Task ProperAwaitTest_ShouldNotTrigger()
    {
        var repository = new DataRepository();
        await repository.InitializeAsync();

        var data = await repository.GetDataAsync();

        Assert.NotNull(data);
        Assert.True(data.Any());
    }

    // SHOULD NOT TRIGGER
    // Animation/UI test with proper suppression (legitimate timing test)
    [Fact]
    [SlopwatchSuppress(
        "SW004",
        "UI animation test validates smooth transitions over 300ms duration as specified in design system. Must use real time delays to verify CSS transitions and frame timing. Visual regression testing requires actual animation completion.",
        "Testing",
        reviewer: "ui-team@company.com")]
    public async Task AnimationTest_WithValidSuppression_ShouldNotTrigger()
    {
        var animator = new UiAnimator();
        var element = new UiElement();

        animator.AnimateFadeIn(element, durationMs: 300);

        // Wait for animation to complete
        await Task.Delay(350);

        Assert.Equal(1.0, element.Opacity);
    }

    // Helper classes for realistic tests
    private class BackgroundService
    {
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
    }

    private class DataProcessor
    {
        public bool IsComplete { get; private set; }
        public void ProcessAsync() => Task.Run(() => IsComplete = true);
    }

    private class MessageQueue
    {
        public int ProcessedCount { get; private set; }
        public void Enqueue(string message) => ProcessedCount++;
    }

    private class BackgroundWorker
    {
        public bool IsComplete { get; private set; }
        public void Start() => Task.Run(() => IsComplete = true);
    }

    private class CacheService
    {
        public bool IsReady { get; private set; }
        public void Initialize() => Task.Run(() => IsReady = true);
    }

    private class AsyncService
    {
        public Task ExecuteAsync() => Task.CompletedTask;
    }

    private class EventAggregator
    {
        public int SubscriberNotifiedCount { get; private set; }
        public void Publish(string eventName) => SubscriberNotifiedCount++;
    }

    private class DatabaseConnection
    {
        public Task ConnectAsync() => Task.CompletedTask;
        public Task<object> ExecuteQueryAsync(string query) => Task.FromResult(new object());
    }

    private class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly int _windowSeconds;
        private int _requestCount;
        private DateTime _windowStart = DateTime.UtcNow;

        public RateLimiter(int maxRequests, int windowSeconds)
        {
            _maxRequests = maxRequests;
            _windowSeconds = windowSeconds;
        }

        public bool TryAcquire()
        {
            if ((DateTime.UtcNow - _windowStart).TotalSeconds > _windowSeconds)
            {
                _requestCount = 0;
                _windowStart = DateTime.UtcNow;
            }

            if (_requestCount >= _maxRequests)
                return false;

            _requestCount++;
            return true;
        }
    }

    private class Debouncer<T>
    {
        private readonly int _delayMs;
        public Debouncer(int delayMs) => _delayMs = delayMs;
        public void Debounce(T value, Action<T> action) => Task.Delay(_delayMs).ContinueWith(_ => action(value));
    }

    private static string CreateSlowEndpoint(int delayMs) => $"http://slow.api/{delayMs}";

    private class EventDrivenService
    {
        public event Action? OnComplete;
        public void Start() => Task.Run(() => OnComplete?.Invoke());
    }

    private class ThreadedService
    {
        public bool IsReady { get; private set; }
        public event Action? OnReady;
        public void Initialize() => Task.Run(() => { IsReady = true; OnReady?.Invoke(); });
    }

    private class LongRunningService
    {
        public Task<ExecutionResult> ExecuteAsync(CancellationToken ct) =>
            Task.FromResult(new ExecutionResult { Success = true });
    }

    private class ExecutionResult
    {
        public bool Success { get; set; }
    }

    private class Calculator
    {
        public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
    }

    private class DataRepository
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<List<string>> GetDataAsync() => Task.FromResult(new List<string> { "data" });
    }

    private class UiAnimator
    {
        public void AnimateFadeIn(UiElement element, int durationMs) =>
            Task.Delay(durationMs).ContinueWith(_ => element.Opacity = 1.0);
    }

    private class UiElement
    {
        public double Opacity { get; set; }
    }
}
