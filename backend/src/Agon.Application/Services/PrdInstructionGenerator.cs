using System.Text;
using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;

namespace Agon.Application.Services;

/// <summary>
/// Generates Product Requirements Document (PRD) instruction files from Truth Map state.
/// Produces .instructions.md format with YAML frontmatter containing full PRD structure.
/// </summary>
public class PrdInstructionGenerator : IArtifactGenerator
{
    /// <inheritdoc />
    public ArtifactType Type => ArtifactType.Prd;

    /// <inheritdoc />
    public string Generate(TruthMapState truthMap)
    {
        ArgumentNullException.ThrowIfNull(truthMap);

        var builder = new StringBuilder();

        AppendYamlFrontmatter(builder);
        AppendHeader(builder);
        AppendExecutiveSummary(builder, truthMap);
        AppendProblemStatement(builder, truthMap);
        AppendSuccessMetrics(builder, truthMap);
        AppendConstraints(builder, truthMap);
        AppendKeyAssumptions(builder, truthMap);
        AppendRisks(builder, truthMap);
        AppendOpenQuestions(builder, truthMap);

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
        builder.AppendLine("# Product Requirements Document");
    }

    private static void AppendExecutiveSummary(StringBuilder builder, TruthMapState truthMap)
    {
        if (string.IsNullOrWhiteSpace(truthMap.CoreIdea))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Executive Summary");
        builder.AppendLine();
        builder.AppendLine(truthMap.CoreIdea);
    }

    private static void AppendProblemStatement(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Personas.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Problem Statement");
        builder.AppendLine();
        builder.AppendLine("### Target Users");

        foreach (var persona in truthMap.Personas)
        {
            builder.AppendLine();
            builder.AppendLine($"**{persona.Name}**");
            builder.AppendLine();
            builder.AppendLine(persona.Description);
        }
    }

    private static void AppendSuccessMetrics(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.SuccessMetrics.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Success Metrics");
        builder.AppendLine();
        builder.AppendLine("| Metric | Target |");
        builder.AppendLine("|---|---|");

        foreach (var metric in truthMap.SuccessMetrics)
        {
            builder.AppendLine($"| {metric} | TBD |");
        }
    }

    private static void AppendConstraints(StringBuilder builder, TruthMapState truthMap)
    {
        var hasConstraints = !string.IsNullOrWhiteSpace(truthMap.Constraints.Budget)
            || !string.IsNullOrWhiteSpace(truthMap.Constraints.Timeline)
            || truthMap.Constraints.TechStack.Count > 0
            || truthMap.Constraints.NonNegotiables.Count > 0;

        if (!hasConstraints)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Constraints");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(truthMap.Constraints.Budget))
        {
            builder.AppendLine($"**Budget:** {truthMap.Constraints.Budget}");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(truthMap.Constraints.Timeline))
        {
            builder.AppendLine($"**Timeline:** {truthMap.Constraints.Timeline}");
            builder.AppendLine();
        }

        if (truthMap.Constraints.TechStack.Count > 0)
        {
            builder.AppendLine("### Technical Stack");
            builder.AppendLine();
            foreach (var tech in truthMap.Constraints.TechStack)
            {
                builder.AppendLine($"- {tech}");
            }
            builder.AppendLine();
        }

        if (truthMap.Constraints.NonNegotiables.Count > 0)
        {
            builder.AppendLine("### Non-Negotiable Requirements");
            builder.AppendLine();
            foreach (var item in truthMap.Constraints.NonNegotiables)
            {
                builder.AppendLine($"- {item}");
            }
        }
    }

    private static void AppendKeyAssumptions(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Assumptions.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Key Assumptions");
        builder.AppendLine();
        builder.AppendLine("| Assumption | Validation Step | Status |");
        builder.AppendLine("|---|---|---|");

        foreach (var assumption in truthMap.Assumptions)
        {
            var statusIcon = assumption.Status switch
            {
                AssumptionStatus.Validated => "✅",
                AssumptionStatus.Invalidated => "❌",
                _ => "⚠️"
            };
            var validation = string.IsNullOrWhiteSpace(assumption.ValidationStep)
                ? "TBD"
                : assumption.ValidationStep;

            builder.AppendLine($"| {assumption.Text} | {validation} | {statusIcon} |");
        }
    }

    private static void AppendRisks(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Risks.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Risks");

        var sortedRisks = truthMap.Risks
            .OrderByDescending(r => r.Severity)
            .ToList();

        foreach (var risk in sortedRisks)
        {
            builder.AppendLine();
            builder.AppendLine($"### {risk.Text}");
            builder.AppendLine();
            builder.AppendLine($"**Category:** {risk.Category}");
            builder.AppendLine();
            builder.AppendLine($"**Severity:** {risk.Severity}");
            builder.AppendLine();
            builder.AppendLine($"**Likelihood:** {risk.Likelihood}");

            if (!string.IsNullOrWhiteSpace(risk.Mitigation))
            {
                builder.AppendLine();
                builder.AppendLine($"**Mitigation:** {risk.Mitigation}");
            }
        }
    }

    private static void AppendOpenQuestions(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.OpenQuestions.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Open Questions");
        builder.AppendLine();

        // Show blocking questions first
        var sortedQuestions = truthMap.OpenQuestions
            .OrderByDescending(q => q.Blocking)
            .ToList();

        foreach (var question in sortedQuestions)
        {
            var blockingIndicator = question.Blocking ? " 🚫 Blocking" : "";
            builder.AppendLine($"- {question.Text}{blockingIndicator}");
        }
    }
}
