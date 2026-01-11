using CommandLine;
using Slopwatch.Detection;
using Slopwatch.Detection.Rules;

namespace Slopwatch.Cmd.Commands;

/// <summary>
/// Command for listing all available detection rules.
/// </summary>
[Verb("list-rules", HelpText = "List all available detection rules")]
public sealed class ListRulesCommand
{
    /// <summary>
    /// Executes the list-rules command.
    /// </summary>
    /// <returns>Exit code: 0 = success, 2 = error</returns>
    public Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all rules
            var rules = new IDetectionRule[]
            {
                new DisabledTestRule(),
                new WarningSuppressRule(),
                new EmptyCatchBlockRule(),
                new TimeoutJigglingRule()
            };

            Console.WriteLine("Available Detection Rules:");
            Console.WriteLine();

            // Print header
            Console.WriteLine($"{"ID",-8} {"Severity",-10} {"Name",-30} Description");
            Console.WriteLine(new string('-', 100));

            // Print each rule
            foreach (var rule in rules.OrderBy(r => r.RuleId))
            {
                var severity = rule.DefaultSeverity.ToString();
                var description = rule.Description.Length > 50
                    ? rule.Description.Substring(0, 47) + "..."
                    : rule.Description;

                Console.WriteLine($"{rule.RuleId,-8} {severity,-10} {rule.Name,-30} {description}");
            }

            Console.WriteLine();
            Console.WriteLine($"Total rules: {rules.Length}");

            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error listing rules: {ex.Message}");
            return Task.FromResult(2);
        }
    }
}
