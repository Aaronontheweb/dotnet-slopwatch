# Slopwatch Suppression Mechanisms

Slopwatch provides three levels of suppression to handle legitimate exceptions to its detection rules:

1. **Attribute-based suppression** - Fine-grained, code-level suppression
2. **Inline comment suppression** - Lightweight, comment-based suppression
3. **Configuration file suppression** - Project-wide, pattern-based suppression

## 1. Attribute-Based Suppression

Use `SlopwatchSuppressAttribute` for fine-grained control at the method, class, property, or field level.

### Syntax

```csharp
[SlopwatchSuppress(
    "SW001",  // Rule ID
    "This test requires Windows COM components not available in Linux CI environment",  // Justification (min 20 chars)
    "Platform",  // Category
    issueUrl: "https://github.com/myorg/myrepo/issues/1234",  // Optional
    reviewer: "john.doe@company.com"  // Optional
)]
[Fact(Skip = "Windows-only test")]
public void TestWindowsComInterop()
{
    // Test implementation
}
```

### Valid Categories

- `Platform` - Platform-specific code (Windows-only, Linux-only, etc.)
- `Interop` - Native code, COM, P/Invoke, unmanaged memory
- `External` - External dependencies not available (database, API, etc.)
- `Generated` - Auto-generated code (EF migrations, gRPC, etc.)
- `TDD` - Test-driven development (feature not yet implemented)
- `Performance` - Performance-critical code requiring unsafe patterns
- `Testing` - Test infrastructure or testing-specific patterns
- `Legacy` - Legacy code with planned refactoring
- `Library` - Third-party library quirks or bugs

### Requirements

- **Justification**: Minimum 20 characters, must be specific and meaningful
- **Category**: Must be one of the valid categories above
- **Rejected phrases**: "false positive", "not needed", "legacy code", "hack", etc.

## 2. Inline Comment Suppression

Use inline comments for lightweight suppression without modifying code signatures.

### Single-Line Suppression

Suppresses the rule for the immediately following line of code:

```csharp
// slopwatch-ignore: SW001 This is a platform-specific test requiring Windows COM
[Fact(Skip = "Windows only")]
public void TestWindowsCom() { }
```

### Block Suppression

Suppresses the rule for a range of lines:

```csharp
// slopwatch-ignore-start: SW002 Legacy interop code requires unsafe
#pragma warning disable CS0618
// ... legacy code ...
#pragma warning restore CS0618
// slopwatch-ignore-end
```

### Format Rules

- **Directive**: `slopwatch-ignore:` or `slopwatch-ignore-start:`
- **Case-insensitive**: `SLOPWATCH-IGNORE`, `Slopwatch-Ignore`, etc. all work
- **Whitespace-tolerant**: Extra spaces are allowed
- **Justification**: Minimum 20 characters (same as attribute)
- **Rule ID**: Must match format `SW###` (e.g., SW001, SW002)

### Examples

```csharp
// ✅ VALID - Sufficient justification
// slopwatch-ignore: SW004 Testing rate limiter behavior requires actual time delays

// ❌ INVALID - Justification too short (less than 20 chars)
// slopwatch-ignore: SW004 Rate limit test

// ✅ VALID - Block suppression
// slopwatch-ignore-start: SW002 Generated code from legacy protobuf compiler
#pragma warning disable CS0618
// ... generated code ...
#pragma warning restore CS0618
// slopwatch-ignore-end
```

## 3. Configuration File Suppression

Use `.slopwatch/config.json` for project-wide, pattern-based suppressions.

### Configuration File Location

Create a file at `.slopwatch/config.json` in your repository root.

### Example Configuration

```json
{
  "suppressions": [
    {
      "ruleId": "SW002",
      "pattern": "**/Generated/**",
      "justification": "Generated code from protobuf compiler - cannot be modified"
    },
    {
      "ruleId": "SW003",
      "pattern": "src/Legacy/**",
      "justification": "Legacy code scheduled for refactoring in Q2 2026 - JIRA-1234",
      "expiresAt": "2026-06-01",
      "issueUrl": "https://jira.company.com/browse/PROJ-1234",
      "reviewer": "john.doe@company.com"
    },
    {
      "ruleId": "SW001",
      "pattern": "**/*.Designer.cs",
      "justification": "WinForms/WPF designer-generated files"
    }
  ],
  "globalSuppressions": [
    {
      "ruleId": "SW004",
      "justification": "Integration test project uses real async coordination and timing delays",
      "issueUrl": "https://github.com/org/repo/issues/123"
    }
  ]
}
```

### Path Suppression Properties

- `ruleId` (required): The rule ID to suppress (e.g., "SW001")
- `pattern` (required): Glob pattern for matching files
  - `**/Generated/**` - All files in Generated directories
  - `src/Legacy/**/*.cs` - All C# files in Legacy directory
  - `**/*.Designer.cs` - All designer-generated files
- `justification` (required): Minimum 20 characters, meaningful explanation
- `issueUrl` (optional): Link to issue tracker (GitHub, Jira, etc.)
- `expiresAt` (optional): Expiration date in `yyyy-MM-dd` format
- `reviewer` (optional): Person who approved the suppression

### Global Suppression Properties

- `ruleId` (required): The rule ID to suppress globally
- `justification` (required): Minimum 20 characters, meaningful explanation
- `issueUrl` (optional): Link to issue tracker
- `expiresAt` (optional): Expiration date in `yyyy-MM-dd` format
- `reviewer` (optional): Person who approved the suppression

### Glob Pattern Examples

```json
{
  "suppressions": [
    // All files in any "Generated" directory
    { "pattern": "**/Generated/**" },

    // All C# files in specific directory
    { "pattern": "src/Legacy/**/*.cs" },

    // All designer files
    { "pattern": "**/*.Designer.cs" },

    // Specific file
    { "pattern": "src/MyProject/MyFile.cs" },

    // All files in root "bin" directory
    { "pattern": "bin/**" }
  ]
}
```

### Expiration Feature

Suppressions can have an expiration date. After this date, the suppression is flagged as expired:

```json
{
  "ruleId": "SW003",
  "pattern": "src/Legacy/**",
  "justification": "Legacy code scheduled for refactoring in Q2 2026",
  "expiresAt": "2026-06-01",
  "issueUrl": "https://jira.company.com/browse/PROJ-1234"
}
```

This helps prevent "forgotten" suppressions from persisting indefinitely.

## Suppression Priority

When multiple suppression mechanisms are present, they are checked in this order:

1. **Attribute suppression** - Most specific, highest priority
2. **Inline comment suppression** - Code-level suppression
3. **Configuration file suppression** - Project-wide patterns

If any suppression matches, the detection is suppressed.

## Best Practices

### When to Use Each Mechanism

- **Attribute**:
  - Production code requiring permanent suppression
  - High-risk suppressions needing code review
  - Error-severity rules (SW001, SW003, etc.)

- **Inline Comment**:
  - Test code with legitimate patterns
  - Temporary suppressions during development
  - Quick fixes without modifying signatures

- **Configuration File**:
  - Generated code directories
  - Third-party library integration
  - Project-wide patterns (migrations, designer files)
  - Legacy code scheduled for refactoring

### Justification Guidelines

Good justifications:
- "Test requires Windows COM components not available in Linux CI environment"
- "P/Invoke signature for kernel32.dll ReadProcessMemory requires unsafe code"
- "Testing rate limiter behavior requires actual time delays to validate throttling"
- "Optional configuration file - FileNotFoundException is expected and handled with defaults"

Bad justifications (will be rejected):
- "false positive"
- "not needed"
- "legacy code"
- "hack"
- "temporary"

### Reviewer Guidelines

For high-risk suppressions:
- Error-severity rules (SW001, SW003, SW006, SW012, etc.)
- Security-sensitive code
- Production code suppressions

Consider including a reviewer:
```csharp
[SlopwatchSuppress(
    "SW003",
    "Optional configuration file - FileNotFoundException is expected",
    "External",
    reviewer: "security-team@company.com"
)]
```

Or in config:
```json
{
  "ruleId": "SW003",
  "pattern": "src/Security/**",
  "justification": "Security-critical code reviewed by security team",
  "reviewer": "security-team@company.com"
}
```

## Rule IDs

- **SW001**: Disabled Test Detection
- **SW002**: Warning Suppression Detection
- **SW003**: Empty Catch Block Detection
- **SW004**: Test Timeout Jiggling Detection

## Migration Guide

### From Attribute-Only to Multiple Mechanisms

If you have existing `SlopwatchSuppressAttribute` usage, you can gradually migrate:

1. Keep critical production suppressions as attributes
2. Move test-specific suppressions to inline comments
3. Move pattern-based suppressions to config file

### Example Migration

Before (all attributes):
```csharp
[SlopwatchSuppress("SW001", "Platform-specific test", "Platform")]
[Fact(Skip = "Windows only")]
public void WindowsTest() { }

[SlopwatchSuppress("SW002", "Generated code", "Generated")]
public class GeneratedClass { }
```

After (mixed approach):
```csharp
// slopwatch-ignore: SW001 Platform-specific test requiring Windows COM
[Fact(Skip = "Windows only")]
public void WindowsTest() { }

// In .slopwatch/config.json:
{
  "suppressions": [
    {
      "ruleId": "SW002",
      "pattern": "**/Generated/**",
      "justification": "Generated code from protobuf compiler"
    }
  ]
}
```

## Troubleshooting

### Suppression Not Working

1. **Check justification length**: Must be at least 20 characters
2. **Check rule ID format**: Must match `SW###` (e.g., SW001, not sw1)
3. **Check pattern syntax**: Use glob patterns like `**/*.cs`, not regex
4. **Check config file location**: Must be at `.slopwatch/config.json` in repository root
5. **Check expiration date**: Suppressions with past `expiresAt` dates are ignored

### Common Errors

- "Justification too short" - Add more context (min 20 chars)
- "Rule ID must match format 'SW###'" - Use SW001, SW002, etc.
- "Category not valid" - Use one of the predefined categories
- Config file not loading - Ensure JSON is valid and location is correct

## See Also

- [SlopwatchSuppressAttribute API Documentation](../src/Slopwatch/Suppression/SlopwatchSuppressAttribute.cs)
- [Configuration Format](../src/Slopwatch/Configuration/SlopwatchConfig.cs)
- [Example Configuration](../.slopwatch/config.json.example)
