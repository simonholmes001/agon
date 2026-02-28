using System.Text;
using System.Text.RegularExpressions;
using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Agon.Domain.TruthMap.Entities;

namespace Agon.Application.Services;

/// <summary>
/// Generates architecture instruction files from Truth Map state.
/// Produces .instructions.md format with YAML frontmatter focused on system architecture.
/// </summary>
public partial class ArchitectureInstructionGenerator : IArtifactGenerator
{
    private static readonly HashSet<string> FrontendTechnologies = new(StringComparer.OrdinalIgnoreCase)
    {
        "next.js", "nextjs", "react", "vue", "angular", "svelte", "tailwind", "tailwindcss",
        "framer motion", "framer-motion", "css", "sass", "scss", "shadcn", "shadcn/ui"
    };

    private static readonly HashSet<string> BackendTechnologies = new(StringComparer.OrdinalIgnoreCase)
    {
        ".net", "dotnet", "asp.net", "asp.net core", "node.js", "nodejs", "express",
        "fastapi", "django", "flask", "spring", "spring boot", "go", "golang", "rust"
    };

    private static readonly HashSet<string> RealtimeTechnologies = new(StringComparer.OrdinalIgnoreCase)
    {
        "signalr", "websocket", "websockets", "socket.io", "pusher", "ably"
    };

    private static readonly HashSet<string> PersistenceTechnologies = new(StringComparer.OrdinalIgnoreCase)
    {
        "postgresql", "postgres", "mysql", "sql server", "mongodb", "redis", "sqlite",
        "dynamodb", "cosmos db", "cosmosdb", "cassandra", "elasticsearch", "pgvector"
    };

    /// <inheritdoc />
    public ArtifactType Type => ArtifactType.Architecture;

    /// <inheritdoc />
    public string Generate(TruthMapState truthMap)
    {
        ArgumentNullException.ThrowIfNull(truthMap);

        var builder = new StringBuilder();

        AppendYamlFrontmatter(builder);
        AppendHeader(builder, truthMap);
        AppendHighLevelTopology(builder, truthMap);
        AppendArchitecturalConstraints(builder, truthMap);
        AppendKeyDecisions(builder, truthMap);
        AppendTechnicalRisks(builder, truthMap);

        return builder.ToString();
    }

    private static void AppendYamlFrontmatter(StringBuilder builder)
    {
        builder.AppendLine("---");
        builder.AppendLine("applyTo: '**'");
        builder.AppendLine("---");
    }

    private static void AppendHeader(StringBuilder builder, TruthMapState truthMap)
    {
        var projectName = ExtractProjectName(truthMap.CoreIdea);

        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            builder.AppendLine($"# {projectName} Architecture");
        }
        else
        {
            builder.AppendLine("# Architecture");
        }
    }

    private static string? ExtractProjectName(string coreIdea)
    {
        if (string.IsNullOrWhiteSpace(coreIdea))
        {
            return null;
        }

        // Try to extract project name from patterns like "ProjectName - description" or "ProjectName: description"
        var match = ProjectNamePattern().Match(coreIdea);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    [GeneratedRegex(@"^([A-Z][a-zA-Z0-9]+(?:\s+[A-Z][a-zA-Z0-9]+)?)\s*[-:–—]")]
    private static partial Regex ProjectNamePattern();

    private static void AppendHighLevelTopology(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Constraints.TechStack.Count == 0)
        {
            return;
        }

        var categorised = CategoriseTechnologies(truthMap.Constraints.TechStack);

        if (categorised.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## High-Level Topology");
        builder.AppendLine();
        builder.AppendLine("| Layer | Technology |");
        builder.AppendLine("|---|---|");

        foreach (var (layer, technologies) in categorised)
        {
            builder.AppendLine($"| {layer} | {string.Join(", ", technologies)} |");
        }
    }

    private static Dictionary<string, List<string>> CategoriseTechnologies(List<string> techStack)
    {
        var result = new Dictionary<string, List<string>>();

        var frontend = new List<string>();
        var backend = new List<string>();
        var realtime = new List<string>();
        var persistence = new List<string>();
        var other = new List<string>();

        foreach (var tech in techStack)
        {
            var normalised = tech.Trim();

            if (FrontendTechnologies.Any(ft => normalised.Contains(ft, StringComparison.OrdinalIgnoreCase)))
            {
                frontend.Add(normalised);
            }
            else if (BackendTechnologies.Any(bt => normalised.Contains(bt, StringComparison.OrdinalIgnoreCase)))
            {
                backend.Add(normalised);
            }
            else if (RealtimeTechnologies.Any(rt => normalised.Contains(rt, StringComparison.OrdinalIgnoreCase)))
            {
                realtime.Add(normalised);
            }
            else if (PersistenceTechnologies.Any(pt => normalised.Contains(pt, StringComparison.OrdinalIgnoreCase)))
            {
                persistence.Add(normalised);
            }
            else
            {
                other.Add(normalised);
            }
        }

        if (frontend.Count > 0) result["Frontend"] = frontend;
        if (backend.Count > 0) result["Backend"] = backend;
        if (realtime.Count > 0) result["Realtime"] = realtime;
        if (persistence.Count > 0) result["Persistence"] = persistence;
        if (other.Count > 0) result["Other"] = other;

        return result;
    }

    private static void AppendArchitecturalConstraints(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Constraints.NonNegotiables.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Architectural Constraints");
        builder.AppendLine();
        foreach (var constraint in truthMap.Constraints.NonNegotiables)
        {
            builder.AppendLine($"- {constraint}");
        }
    }

    private static void AppendKeyDecisions(StringBuilder builder, TruthMapState truthMap)
    {
        if (truthMap.Decisions.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Key Architecture Decisions");

        foreach (var decision in truthMap.Decisions)
        {
            builder.AppendLine();
            builder.AppendLine($"### {decision.Text}");
            builder.AppendLine();
            builder.AppendLine($"**Rationale:** {decision.Rationale}");
            builder.AppendLine();
            builder.AppendLine($"**Status:** {(decision.Binding ? "🔒 Binding" : "Advisory")}");
        }
    }

    private static void AppendTechnicalRisks(StringBuilder builder, TruthMapState truthMap)
    {
        var technicalRisks = truthMap.Risks
            .Where(r => r.Category == RiskCategory.Technical)
            .OrderByDescending(r => r.Severity)
            .ToList();

        if (technicalRisks.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Technical Risks");

        foreach (var risk in technicalRisks)
        {
            builder.AppendLine();
            builder.AppendLine($"### {risk.Text}");
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
}
