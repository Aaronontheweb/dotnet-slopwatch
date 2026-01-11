# Slopwatch Project Instructions

## Project Overview
Slopwatch is a .NET tool that detects LLM "reward hacking" behaviors in code changes. It runs as a Claude Code hook or in CI/CD pipelines to catch when LLMs take shortcuts to make tests pass without actually fixing issues.

## Build Commands
```bash
# Restore tools and dependencies
dotnet tool restore
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Pack as tool
dotnet pack
```

## Project Structure
```
src/
  Slopwatch/              # Core library with detection rules
  Slopwatch.Cmd/          # CLI tool (dotnet tool)
tests/
  Slopwatch.Tests/        # Unit and integration tests
  Slopwatch.Tests.Fixtures/  # Intentionally "sloppy" code samples
```

## Detection Rules (SW### format)
- **SW001**: Disabled tests (`[Fact(Skip=...)]`, `[Ignore]`, `#if false`)
- **SW002**: Warning suppression (`#pragma warning disable`, `[SuppressMessage]`)
- **SW003**: Empty catch blocks (swallowing exceptions)
- **SW004**: Timeout jiggling (`Task.Delay`, `Thread.Sleep` in tests)
- Additional rules to be discovered through research

## Development Guidelines

### Git Workflow
- All work must be done in feature branches
- Submit all changes as pull requests with CI/CD passing
- Never commit directly to `dev` or `main` branches

### Code Style
- Use C# 13+ features where appropriate
- Enable nullable reference types
- Treat warnings as errors
- Follow standard .NET naming conventions

### Testing Requirements
- All detection rules must have unit tests
- Use xUnit as the test framework
- Include test fixtures with intentionally sloppy code to verify detection
- Integration tests should use real git repositories

### Architecture Patterns
- Use Roslyn for C# syntax analysis
- Use libgit2sharp for git diff analysis (follow Incrementalist patterns)
- Detection rules implement `IDetectionRule` interface
- Support multiple output formats: console, JSON, SARIF

## Claude Code Hook Integration
The tool runs as a Claude Code hook using the `--hook` flag:
```bash
slopwatch analyze -d . --hook
```

The `--hook` flag outputs errors to stderr, suppresses other output, and exits with code 2 on failure (blocking the edit).

Hook configuration goes in `.claude/settings.json` at the project level. Use `--verbose` flag if you want to see baseline loading details in normal mode.

### When Slopwatch Blocks Your Edit
If you see "SLOPWATCH BLOCKED" in a hook error, your edit introduced a "reward hacking" pattern. These are shortcuts that make tests pass without fixing the real issue:

- **SW001**: Don't disable tests with `Skip` or `#if false`
- **SW002**: Don't suppress warnings with `#pragma warning disable`
- **SW003**: Don't use empty catch blocks that swallow exceptions
- **SW004**: Don't add arbitrary delays (`Task.Delay`, `Thread.Sleep`) in tests
- **SW005**: Don't disable `TreatWarningsAsErrors` or add to `NoWarn`

**How to fix**: Read the specific error message and suggested fix. Implement a proper solution instead of working around the problem. If the suppression is genuinely needed, use `[SlopwatchSuppress("SW###", "justification with 20+ chars")]`.

## CI/CD
- GitHub Actions for PR validation and releases
- Run slopwatch on all PRs to catch slop patterns in contributions

## Dependencies (via Central Package Management)
Key packages needed:
- `LibGit2Sharp` - Git diff analysis
- `Microsoft.CodeAnalysis.CSharp` - Roslyn syntax analysis
- `CommandLineParser` - CLI argument parsing
- `xunit` - Testing framework

## When Implementing
1. Read existing code before modifying
2. Prefer editing existing files over creating new ones
3. Keep changes focused and minimal
4. Run tests after each significant change
5. Use the todo list to track progress
