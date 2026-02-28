using System.Text;
using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;

namespace Agon.Application.Services;

/// <summary>
/// Generates Assumption Validation instruction files from Truth Map state.
/// Produces .instructions.md format with all assumptions, validation steps, and status tracking.
/// </summary>
public class AssumptionValidationGenerator : IArtifactGenerator
{
    /// <inheritdoc />
    public ArtifactType Type => ArtifactType.Assumptions;

    /// <inheritdoc />
    public string Generate(TruthMapState truthMap)
    {
        ArgumentNullException.ThrowIfNull(truthMap);

        var builder = new StringBuilder();

        AppendYamlFrontmatter(builder);
        AppendHeader(builder);

        if (truthMap.Assumptions.Count == 0)
        {
            AppendNoAssumptionsMessage(builder);
        }
        else
        {
            AppendAssumptionTable(builder, truthMap);
            AppendStatusSummary(builder, truthMap);
            AppendCriticalAssumptions(builder, truthMap);
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
        builder.AppendLine("# Assumption Validation");
    }

    private static void AppendNoAssumptionsMessage(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("No assumptions have been documented for this project.");
    }

    private static void AppendAssumptionTable(StringBuilder builder, TruthMapState truthMap)
    {
        builder.AppendLine();
        builder.AppendLine("## All Assumptions");
        builder.AppendLine();
        builder.AppendLine("| ID | Assumption | Validation Step | Status |");
        builder.AppendLine("|---|---|---|---|");

        foreach (var assumption in truthMap.Assumptions)
        {
            var validationStep = string.IsNullOrWhiteSpace(assumption.ValidationStep)
                ? "TBD"
                : assumption.ValidationStep;

            var statusIcon = GetStatusIcon(assumption.Status);

            builder.AppendLine($"| {assumption.Id} | {assumption.Text} | {validationStep} | {statusIcon} |");
        }
    }

    private static void AppendStatusSummary(StringBuilder builder, TruthMapState truthMap)
    {
        var validatedCount = truthMap.Assumptions.Count(a => a.Status == AssumptionStatus.Validated);
        var unvalidatedCount = truthMap.Assumptions.Count(a => a.Status == AssumptionStatus.Unvalidated);
        var invalidatedCount = truthMap.Assumptions.Count(a => a.Status == AssumptionStatus.Invalidated);

        builder.AppendLine();
        builder.AppendLine("## Status Summary");
        builder.AppendLine();
        builder.AppendLine("| Status | Count |");
        builder.AppendLine("|---|---|");
        builder.AppendLine($"| ✅ Validated | {validatedCount} |");
        builder.AppendLine($"| ⚠️ Unvalidated | {unvalidatedCount} |");
        builder.AppendLine($"| ❌ Invalidated | {invalidatedCount} |");
    }

    private static void AppendCriticalAssumptions(StringBuilder builder, TruthMapState truthMap)
    {
        var unvalidated = truthMap.Assumptions
            .Where(a => a.Status == AssumptionStatus.Unvalidated)
            .ToList();

        if (unvalidated.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## ⚠️ Critical: Unvalidated Assumptions");
        builder.AppendLine();
        builder.AppendLine("The following assumptions require validation before proceeding:");
        builder.AppendLine();

        foreach (var assumption in unvalidated)
        {
            builder.AppendLine($"- **{assumption.Text}**");
            if (!string.IsNullOrWhiteSpace(assumption.ValidationStep))
            {
                builder.AppendLine($"  - Validation step: {assumption.ValidationStep}");
            }
        }
    }

    private static string GetStatusIcon(AssumptionStatus status)
    {
        return status switch
        {
            AssumptionStatus.Validated => "✅ Validated",
            AssumptionStatus.Invalidated => "❌ Invalidated",
            _ => "⚠️ Unvalidated"
        };
    }
}
