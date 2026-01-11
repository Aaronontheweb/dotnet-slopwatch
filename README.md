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

### Analyze current directory
```bash
slopwatch analyze -d .
```

### Analyze specific files
```bash
slopwatch analyze -f src/MyProject/MyFile.cs
```

### Use glob patterns
```bash
slopwatch analyze -d . -p "**/*.cs"
```

### Output formats
```bash
# Human-readable console output (default)
slopwatch analyze -d .

# JSON for programmatic use
slopwatch analyze -d . --output json
```

### Exit codes
```bash
# Fail if errors found (default)
slopwatch analyze -d . --fail-on error

# Fail on warnings too
slopwatch analyze -d . --fail-on warning
```

## Detection Rules

| Rule | Description |
|------|-------------|
| SW001 | Disabled tests via Skip, Ignore, or #if false |
| SW002 | Warning suppression via pragma or SuppressMessage |
| SW003 | Empty catch blocks that swallow exceptions |
| SW004 | Arbitrary delays in test code |

## Claude Code Integration

Add slopwatch as a hook to catch slop patterns during AI-assisted coding. Create a file at `.claude/hooks/slopwatch-hook.json`:

```json
{
  "hooks": {
    "Stop": [
      {
        "type": "command",
        "command": "dotnet slopwatch analyze -d . --output json --fail-on error",
        "timeout": 60
      }
    ]
  }
}
```

The hook will run when you stop a coding session, analyzing all C# files in the current directory for slop patterns.

## CI/CD Integration

### GitHub Actions
```yaml
- name: Install Slopwatch
  run: dotnet tool install --global Slopwatch.Cmd

- name: Run Slopwatch
  run: slopwatch analyze -d . --output json --fail-on error
```

### Azure DevOps
```yaml
- task: DotNetCoreCLI@2
  displayName: 'Install Slopwatch'
  inputs:
    command: 'custom'
    custom: 'tool'
    arguments: 'install --global Slopwatch.Cmd'

- script: slopwatch analyze -d . --fail-on error
  displayName: 'Run Slopwatch'
```

## Configuration

Create a `.slopwatch/slopwatch.json` configuration file to customize behavior:

```json
{
  "minSeverity": "warning",
  "rules": {
    "SW001": { "enabled": true, "severity": "error" },
    "SW002": { "enabled": true, "severity": "warning" },
    "SW003": { "enabled": true, "severity": "error" },
    "SW004": { "enabled": true, "severity": "warning" }
  },
  "exclude": ["**/Generated/**", "**/obj/**", "**/bin/**"]
}
```

Use the `-c` or `--config` option to specify a custom configuration file location:

```bash
slopwatch analyze -d . --config path/to/config.json
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
