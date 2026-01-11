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
