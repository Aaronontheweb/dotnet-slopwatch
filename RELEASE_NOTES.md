#### 0.1.1 January 11th 2026 ####

**New Features:**
* Claude Code hook integration with `--hook` flag for exit code 2 on detection (PR #13)
* `--verbose` flag to control baseline loading output visibility (PR #13)
* `--stats` flag for performance metrics and analysis statistics (PR #14)

**Bug Fixes:**
* Fixed default glob pattern handling for CommandLineParser empty IEnumerable issue (PR #13)
* Fixed path handling in SuppressionChecker to resolve relative paths correctly (PR #13)

#### 0.1.0 January 11th 2026 ####

Initial release of Slopwatch - LLM anti-cheat for .NET.

**Features:**
* Detection rules: SW001-SW005 for common LLM reward-hacking patterns
* Baseline mode to detect only NEW slop in existing codebases
* `slopwatch init` command for easy project onboarding
* JSON output format for CI/CD integration
* Configurable severity levels and rule customization
* Multi-level suppression system (attributes, comments, config)

**Detection Rules:**
* SW001: Disabled tests (Skip, Ignore, #if false)
* SW002: Warning suppression (pragma, SuppressMessage)
* SW003: Empty catch blocks
* SW004: Timeout jiggling (Task.Delay, Thread.Sleep in tests)
* SW005: Project file slop (NoWarn, TreatWarningsAsErrors)
