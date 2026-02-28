using System.Text;
using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;

namespace Agon.Application.Services;

/// <summary>
/// Generates Risk Registry instruction files from Truth Map state.
/// Produces .instructions.md format with all risks, mitigations, and summary statistics.
/// </summary>
public class RiskRegistryGenerator : IArtifactGenerator
{
    /// <inheritdoc />
    public ArtifactType Type => ArtifactType.Risks;

    /// <inheritdoc />
    public string Generate(TruthMapState truthMap)
    {
        ArgumentNullException.ThrowIfNull(truthMap);

        var builder = new StringBuilder();

        AppendYamlFrontmatter(builder);
        AppendHeader(builder);

        if (truthMap.Risks.Count == 0)
        {
            AppendNoRisksMessage(builder);
        }
        else
        {
            AppendRiskTable(builder, truthMap);
            AppendCategorySummary(builder, truthMap);
            AppendSeveritySummary(builder, truthMap);
        }

        return builder.ToString();
    }

    private static void AppendYamlFrontmatter(StringBuilder builder)
    {
        builder.AppendLine("---");
        builder.AppendLine("applyTo: '**'");
        builder.AppendLine("---");
    }

    private static void AppendHeader(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("# Risk Registry");
    }

    private static void AppendNoRisksMessage(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("No risks have been identified for this project.");
    }

    private static void AppendRiskTable(StringBuilder builder, TruthMapState truthMap)
    {
        var sortedRisks = truthMap.Risks
            .OrderByDescending(r => r.Severity)
            .ThenByDescending(r => r.Likelihood)
            .ToList();

        builder.AppendLine();
        builder.AppendLine("## All Risks");
        builder.AppendLine();
        builder.AppendLine("| ID | Risk | Category | Severity | Likelihood | Mitigation | Source |");
        builder.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var risk in sortedRisks)
        {
            var mitigation = string.IsNullOrWhiteSpace(risk.Mitigation)
                ? "TBD"
                : risk.Mitigation;

            builder.AppendLine($"| {risk.Id} | {risk.Text} | {risk.Category} | {risk.Severity} | {risk.Likelihood} | {mitigation} | {risk.Agent} |");
        }
    }

    private static void AppendCategorySummary(StringBuilder builder, TruthMapState truthMap)
    {
        var categoryGroups = truthMap.Risks
            .GroupBy(r => r.Category)
            .OrderByDescending(g => g.Count())
            .ToList();

        builder.AppendLine();
        builder.AppendLine("## Summary by Category");
        builder.AppendLine();
        builder.AppendLine("| Category | Count |");
        builder.AppendLine("|---|---|");

        foreach (var group in categoryGroups)
        {
            builder.AppendLine($"| {group.Key} | {group.Count()} |");
        }
    }

    private static void AppendSeveritySummary(StringBuilder builder, TruthMapState truthMap)
    {
        var severityGroups = truthMap.Risks
            .GroupBy(r => r.Severity)
            .OrderByDescending(g => g.Key)
            .ToList();

        builder.AppendLine();
        builder.AppendLine("## Summary by Severity");
        builder.AppendLine();
        builder.AppendLine("| Severity | Count |");
        builder.AppendLine("|---|---|");

        foreach (var group in severityGroups)
        {
            builder.AppendLine($"| {group.Key} | {group.Count()} |");
        }
    }
}
