# Slopwatch

A .NET tool that detects LLM "reward hacking" behaviors in code changes. Runs as a Claude Code hook or in CI/CD pipelines to catch when AI coding assistants take shortcuts instead of properly fixing issues.

## What is "Slop"?

When LLMs generate code, they sometimes take shortcuts that make tests pass or builds succeed without actually solving the underlying problem. These patterns include:

- **Disabling tests** instead of fixing them (`[Fact(Skip="flaky")]`)
- **Suppressing warnings** instead of addressing them (`#pragma warning disable`)
- **Swallowing exceptions** with empty catch blocks
- **Adding arbitrary delays** to mask timing issues (`Task.Delay(1000)`)
- And more...

Slopwatch catches these patterns in your git diffs before they make it into your codebase.

## Installation

```bash
# Install as a global tool
dotnet tool install --global Slopwatch.Cmd

# Or install locally
dotnet tool install Slopwatch.Cmd
```

## Usage

### Analyze changes against a branch
```bash
slopwatch analyze --branch main
```

### Analyze working tree (for hooks)
```bash
slopwatch analyze --working-tree
```

### Output formats
```bash
# Human-readable console output (default)
slopwatch analyze --branch main

# JSON for programmatic use
slopwatch analyze --branch main --output json

# SARIF for GitHub code scanning
slopwatch analyze --branch main --output sarif --file results.sarif
```

### Exit codes
```bash
# Fail if errors found (default)
slopwatch analyze --fail-on error

# Fail on warnings too
slopwatch analyze --fail-on warning
```

## Detection Rules

| Rule | Description |
|------|-------------|
| SW001 | Disabled tests via Skip, Ignore, or #if false |
| SW002 | Warning suppression via pragma or SuppressMessage |
| SW003 | Empty catch blocks that swallow exceptions |
| SW004 | Arbitrary delays in test code |

## Claude Code Integration

Add slopwatch as a hook to catch slop patterns during AI-assisted coding:

```json
{
  "hooks": {
    "Stop": [
      {
        "type": "command",
        "command": "dotnet slopwatch analyze --working-tree --output json --fail-on error"
      }
    ]
  }
}
```

## CI/CD Integration

### GitHub Actions
```yaml
- name: Run Slopwatch
  run: |
    dotnet tool install --global Slopwatch.Cmd
    slopwatch analyze --branch origin/${{ github.base_ref }} --output sarif --file slopwatch.sarif --fail-on error

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: slopwatch.sarif
```

## Configuration

Create `.slopwatch/slopwatch.json`:

```json
{
  "gitBranch": "main",
  "minSeverity": "warning",
  "failOnSeverity": "error",
  "rules": {
    "SW001": { "enabled": true, "severity": "error" },
    "SW002": { "enabled": true, "severity": "warning" }
  },
  "exclude": ["**/Generated/**"]
}
```

## Building from Source

```bash
dotnet build
dotnet test
dotnet pack
```

## Contributing

1. Fork and create a feature branch
2. Make changes and add tests
3. Submit a pull request

Note: This project uses slopwatch on itself - your PR will be analyzed for slop patterns!

## License

Apache 2.0 - see [LICENSE](LICENSE) for details.

## Inspiration

- [Slopometry](https://github.com/TensorTemplar/slopometry) - Python equivalent for Claude Code
- [Incrementalist](https://github.com/petabridge/Incrementalist) - Git diff analysis patterns
