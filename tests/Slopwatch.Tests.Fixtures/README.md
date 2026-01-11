# Slopwatch Test Fixtures

This project contains intentionally sloppy code samples designed to test Slopwatch detection rules. Each file demonstrates both **violations** (code that should trigger rules) and **clean examples** (code that should not trigger rules).

## Purpose

These fixtures serve multiple purposes:

1. **Manual Testing** - Developers can run Slopwatch against these files to verify detection rules work correctly
2. **Documentation** - Real-world examples of what each rule detects and how to properly suppress violations
3. **Regression Testing** - Future automated tests can use these fixtures to ensure rules don't break
4. **Example Reference** - Shows developers what patterns to avoid and how to write proper suppression attributes

## Files

### DisabledTestSamples.cs
**Rule:** SW001 - Disabled Test Detection

Contains examples of:
- `[Fact(Skip = "reason")]` and `[Theory(Skip = "")]` attributes (xUnit)
- `[Ignore]` attributes (NUnit style, commented)
- Tests wrapped in `#if false` preprocessor directives
- Properly suppressed disabled tests with valid `SlopwatchSuppress` attributes

### WarningSuppressSamples.cs
**Rule:** SW002 - Warning Suppression Detection

Contains examples of:
- `#pragma warning disable` without matching `restore`
- `[SuppressMessage]` attributes on methods
- Multiple pragma directives
- Properly scoped pragma blocks with matching restore
- Valid suppressions for generated code, legacy APIs, and third-party library quirks

### EmptyCatchSamples.cs
**Rule:** SW003 - Empty Catch Block Detection

Contains examples of:
- Completely empty `catch` blocks
- Catch blocks with only comments
- Catch blocks that only log without rethrowing
- Overly broad exception catches (`catch(Exception)`, `catch` without type)
- Proper exception handling patterns
- Valid suppressions for optional configuration files and cleanup code

### TimeoutJigglingSamples.cs
**Rule:** SW004 - Test Timeout Jiggling Detection

Contains examples of:
- `Task.Delay()` in test methods
- `Thread.Sleep()` in tests
- `SpinWait` usage in tests
- Proper synchronization patterns (TaskCompletionSource, ManualResetEvent, CancellationToken)
- Valid suppressions for rate limiter tests, debounce tests, and timeout behavior validation

### CleanCodeSamples.cs
**Clean Code Patterns** - Should NOT trigger any rules

Contains examples of:
- Properly written tests without Skip attributes
- Correct exception handling with specific exception types
- Proper async/await patterns
- Correct use of synchronization primitives
- No warning suppression or proper scoping when needed

## Building

This project intentionally disables `TreatWarningsAsErrors` and suppresses specific compiler and xUnit analyzer warnings because the fixture code contains intentional violations.

```bash
cd tests/Slopwatch.Tests.Fixtures
dotnet build
```

## Running Slopwatch Against Fixtures

To test Slopwatch detection rules against these fixtures:

```bash
# Analyze all fixture files
slopwatch analyze tests/Slopwatch.Tests.Fixtures/

# Analyze specific file
slopwatch analyze tests/Slopwatch.Tests.Fixtures/DisabledTestSamples.cs

# Run with specific rule only
slopwatch analyze tests/Slopwatch.Tests.Fixtures/ --rule SW001
```

## Expected Results

When running Slopwatch against these fixtures, you should see:

- **DisabledTestSamples.cs**: Multiple SW001 violations detected for disabled tests without valid suppression
- **WarningSuppressSamples.cs**: Multiple SW002 violations for warning suppressions without valid suppression
- **EmptyCatchSamples.cs**: Multiple SW003 violations for empty/broad catch blocks without valid suppression
- **TimeoutJigglingSamples.cs**: Multiple SW004 violations for test delays without valid suppression
- **CleanCodeSamples.cs**: No violations detected (all clean code)

Methods marked with `SlopwatchSuppress` attributes should NOT trigger violations, demonstrating that the suppression mechanism works correctly.

## Important Notes

1. **Compilation Warnings Suppressed**: This project intentionally suppresses compiler warnings (CS0169, CS8618) and xUnit analyzer warnings (xUnit1013, xUnit1030, xUnit1031) because the fixture code demonstrates anti-patterns.

2. **Test Execution**: While these files contain `[Fact]` and `[Theory]` attributes for realism, they are not meant to be executed as actual tests. They are sample code for detection rule testing.

3. **Realistic Patterns**: The violations in these fixtures are based on real-world patterns found in LLM-generated code and common developer mistakes.

4. **Suppression Examples**: Pay special attention to the `SlopwatchSuppress` attribute usage - these show the proper way to document legitimate exceptions to the rules.
