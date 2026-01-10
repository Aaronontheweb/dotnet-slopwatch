# build-system-template
Akka.NET project build system template that provides standardized build and CI/CD configuration for all Akka.NET projects.

## Build System Overview
This repository contains our standardized build system setup that can be used across all Akka.NET projects. Here are the key components and practices we follow:

### CI/CD Configuration
We primarily use GitHub Actions for our CI/CD pipelines, but also maintain Azure DevOps pipeline examples. You can find the configuration examples in:
- `.github/workflows/` - GitHub Actions pipeline examples
- `.azuredevops/` - Azure DevOps pipeline examples

### SDK Version Management
We use `global.json` to pin the .NET SDK version for both CI/CD environments and local development. This ensures consistent builds across all environments and developers.

### .NET Tools
We use local .NET tools to enhance our build and documentation process. The tools are configured in `.config/dotnet-tools.json` and include:

- [Incrementalist](https://github.com/petabridge/Incrementalist) (v1.0.0-beta4) - Used for determining which projects need to be rebuilt based on Git changes
- [DocFx](https://dotnet.github.io/docfx/) (v2.78.3) - Used for generating documentation

To restore these tools in your local environment, run:
```powershell
dotnet tool restore
```

This command is automatically executed in our CI/CD pipelines (both GitHub Actions and Azure DevOps) to ensure tools are available during builds.

### Centralized Package and Build Management
We utilize two key MSBuild files for centralized configuration:

1. `Directory.Packages.props` - Implements [Central Package Version Management](https://learn.microsoft.com/nuget/consume-packages/Central-Package-Management) for consistent NuGet package versions across all projects in the solution.

2. `Directory.Build.props` - Defines common build properties, including:
   - Copyright and author information
   - Source linking configuration
   - NuGet package metadata
   - Common compiler settings
   - Target framework definitions

### Code Coverage Configuration
The `coverlet.runsettings` file configures code coverage collection using Coverlet, with settings for:
- Multiple coverage report formats (JSON, Cobertura, LCOV, TeamCity, OpenCover)
- Test assembly exclusions
- Source linking integration
- Performance optimizations

### Release Management
Our release process is streamlined through:
- `RELEASE_NOTES.md` - Contains version history and release notes
- `build.ps1` - PowerShell script that processes release notes and updates version information
- Supporting scripts in `/scripts`:
  - `bumpVersion.ps1` - Updates version numbers
  - `getReleaseNotes.ps1` - Parses release notes

The build system primarily relies on standard `dotnet` CLI commands, with the PowerShell scripts mainly handling release note processing and version management.

### Solution Format
We prefer the new `.slnx` XML-based solution format over the traditional `.sln` format. This requires .NET 9 SDK or later. The new format is more concise and easier to work with. You can migrate existing solutions using:

```powershell
dotnet sln migrate
```

For more information about the new `.slnx` format, see the [official announcement](https://devblogs.microsoft.com/dotnet/introducing-slnx-support-dotnet-cli/).

## Getting Started
1. Ensure you have the correct .NET SDK version installed (check `global.json`)
2. Clone this repository
3. Run `dotnet build` to verify the build system
4. Customize the configuration files for your specific project needs
