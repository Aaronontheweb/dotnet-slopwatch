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
The tool can run as a Claude Code hook:
```bash
dotnet slopwatch analyze --working-tree --output json --fail-on error
```

Hook configuration goes in `.claude/hooks/` or project-level hooks.

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
