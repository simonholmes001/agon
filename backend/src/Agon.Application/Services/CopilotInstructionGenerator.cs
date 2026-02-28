using System.Text;
using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;

namespace Agon.Application.Services;

/// <summary>
/// Generates GitHub Copilot instruction files from Truth Map state.
/// Transforms session artifacts into .instructions.md format with YAML frontmatter.
/// </summary>
public class CopilotInstructionGenerator : IArtifactGenerator
{
    private readonly CopilotInstructionOptions _defaultOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotInstructionGenerator"/> class.
    /// </summary>
    /// <param name="defaultOptions">Default options for generation. If null, uses default settings.</param>
    public CopilotInstructionGenerator(CopilotInstructionOptions? defaultOptions = null)
    {
        _defaultOptions = defaultOptions ?? new CopilotInstructionOptions();
    }

    /// <inheritdoc />
    public ArtifactType Type => ArtifactType.Copilot;

    /// <inheritdoc />
    public string Generate(TruthMapState truthMap) => Generate(truthMap, null);

    /// <summary>
    /// Generates a GitHub Copilot instruction file from the Truth Map.
    /// </summary>
    /// <param name="truthMap">The Truth Map state to transform.</param>
    /// <param name="options">Optional configuration for the output format. If null, uses default options.</param>
    /// <returns>A Markdown string with YAML frontmatter suitable for .github/copilot-instructions.md</returns>
    public string Generate(TruthMapState truthMap, CopilotInstructionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(truthMap);

        options ??= _defaultOptions;
        var builder = new StringBuilder();

        AppendYamlFrontmatter(builder, options);
        AppendHeader(builder, truthMap);
        AppendProjectOverview(builder, truthMap);
        AppendConstraints(builder, truthMap);
        AppendSuccessMetrics(builder, truthMap);
        AppendPersonas(builder, truthMap);
        AppendDecisions(builder, truthMap);
        AppendRisks(builder, truthMap);
        AppendAssumptions(builder, truthMap);

        return builder.ToString();
    }

    private static void AppendYamlFrontmatter(StringBuilder builder, CopilotInstructionOptions options)
    {
        builder.AppendLine("---");
        builder.AppendLine($"applyTo: '{options.ApplyTo}'");
        builder.AppendLine("---");
    }

    private static void AppendHeader(StringBuilder builder, TruthMapState truthMap)
    {
        // Only show header if there's no content
        var hasContent = !string.IsNullOrWhiteSpace(truthMap.CoreIdea)
            || HasConstraints(truthMap.Constraints)
            || truthMap.SuccessMetrics.Count > 0
            || truthMap.Personas.Count > 0
            || truthMap.Decisions.Count > 0
            || truthMap.Risks.Count > 0
            || truthMap.Assumptions.Count > 0;

        if (!hasContent)
        {
            builder.AppendLine();
            builder.AppendLine("# Development Instructions");
        }
    }

    private static void AppendProjectOverview(StringBuilder builder, TruthMapState truthMap)
    {
        if (string.IsNullOrWhiteSpace(truthMap.CoreIdea))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("# Project Overview");
        builder.AppendLine();
        builder.AppendLine(truthMap.CoreIdea);
    }

    private static void AppendConstraints(StringBuilder builder, TruthMapState truthMap)
    {
        var constraints = truthMap.Constraints;
        if (!HasConstraints(constraints))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Constraints");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(constraints.Budget))
        {
            builder.AppendLine($"**Budget:** {constraints.Budget}");
        }

        if (!string.IsNullOrWhiteSpace(constraints.Timeline))
        {
            builder.AppendLine($"**Timeline:** {constraints.Timeline}");
        }

        if (constraints.TechStack.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Tech Stack");
            builder.AppendLine();
            foreach (var tech in constraints.TechStack)
            {
                builder.AppendLine($"- {tech}");
            }
        }

        if (constraints.NonNegotiables.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Non-Negotiables");
            builder.AppendLine();
            foreach (var item in constraints.NonNegotiables)
            {
                builder.AppendLine($"- {item}");
            }
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
        foreach (var metric in truthMap.SuccessMetrics)
        {
            builder.AppendLine($"- {metric}");
        }
    }

    private static void AppendPersonas(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Personas.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Target Users");
        foreach (var persona in truthMap.Personas)
        {
            builder.AppendLine();
            builder.AppendLine($"### {persona.Name}");
            builder.AppendLine();
            builder.AppendLine(persona.Description);
        }
    }

    private static void AppendDecisions(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Decisions.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Key Decisions");
        foreach (var decision in truthMap.Decisions)
        {
            builder.AppendLine();
            builder.AppendLine($"### {decision.Text}");
            builder.AppendLine();
            builder.AppendLine($"**Rationale:** {decision.Rationale}");
            builder.AppendLine();
            builder.AppendLine($"**Status:** {(decision.Binding ? "Binding" : "Advisory")}");
        }
    }

    private static void AppendRisks(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Risks.Count == 0)
        {
            return;
        }

        // Sort by severity descending (Critical > High > Medium > Low)
        var sortedRisks = truthMap.Risks
            .OrderByDescending(r => r.Severity)
            .ToList();

        builder.AppendLine();
        builder.AppendLine("## Risks & Mitigations");
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

    private static void AppendAssumptions(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Assumptions.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Assumptions");
        foreach (var assumption in truthMap.Assumptions)
        {
            builder.AppendLine();
            builder.AppendLine($"### {assumption.Text}");

            if (!string.IsNullOrWhiteSpace(assumption.ValidationStep))
            {
                builder.AppendLine();
                builder.AppendLine($"**Validation:** {assumption.ValidationStep}");
            }

            builder.AppendLine();
            var statusIcon = assumption.Status switch
            {
                AssumptionStatus.Validated => "✅ Validated",
                AssumptionStatus.Invalidated => "❌ Invalidated",
                _ => "⚠️ Unvalidated"
            };
            builder.AppendLine($"**Status:** {statusIcon}");
        }
    }

    private static bool HasConstraints(Constraints constraints)
    {
        return !string.IsNullOrWhiteSpace(constraints.Budget)
            || !string.IsNullOrWhiteSpace(constraints.Timeline)
            || constraints.TechStack.Count > 0
            || constraints.NonNegotiables.Count > 0;
    }
}
