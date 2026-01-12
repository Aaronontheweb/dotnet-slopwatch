#### 0.3.0 January 12th 2026 ####

**Compatibility:**
* Downgraded to .NET 8 for broader compatibility across environments (PR #34)

**Bug Fixes:**
* MultiEdit tool now properly analyzed in Claude Code hook mode (PR #32)

**Documentation:**
* Improved hook error messages for clarity when working with programming assistants (PR #35)

#### 0.2.1 January 11th 2026 ####

**New Features:**
* Added SW006 detection rule for Central Package Management (CPM) version override abuse (PR #25)
  - Detects `VersionOverride` attribute on `PackageReference` (always flagged as it explicitly bypasses CPM)
  - Detects `Version` attribute on `PackageReference` when CPM is enabled via `Directory.Packages.props` or `ManagePackageVersionsCentrally` setting
  - Context-aware detection that only flags violations when CPM is actually in use
* MSBuild `.props` and `.targets` files now analyzed by default (PR #24)
  - Enables SW005 rule to detect warning suppression in `Directory.Build.props`, `Directory.Build.targets`, and custom MSBuild files
  - Previously required explicit `-f` flag to analyze these files

#### 0.2.0 January 11th 2026 ####

**Performance Improvements:**
* Hook mode now uses `git status` to analyze only dirty files - near-instant analysis (~340ms vs ~16s on large repos) (PR #20)
* Parallel analysis with Akka.Streams for CI/CD mode - 2.4x speedup on large codebases (6.62s vs 15.89s on Akka.NET's 2225 files) (PR #22)
* New `--parallel` flag to control parallelism (default = processor count, 0 = sequential) (PR #22)
* Parallel analysis activates automatically when analyzing >50 files (PR #22)

**Bug Fixes:**
* Fixed bug where hook mode git status optimization wasn't triggering due to CommandLineParser initializing IEnumerable to empty instead of null (PR #21)
* Git worktrees now excluded from analysis by default to prevent duplicate scanning (PR #19)
* SW003 rule no longer flags legitimate `catch(Exception)` blocks with actual handling code - only empty catches and logging-only catches are flagged (PR #18, fixes #17)

**Compatibility:**
* Added `RollForward=LatestMajor` to allow running on newer .NET versions (PR #16)

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
