using System.Text.Json;
using System.Xml.Linq;

namespace Marmotte.ChatUi.Architecture.Tests;

/// <summary>Verifies project dependency boundaries and keeps infrastructure packages out of the core and component library.</summary>
public sealed class DependencyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["Domain"] = [],
        ["Contracts"] = [],
        ["Application"] = ["Domain", "Contracts"],
        ["Components"] = ["Contracts"],
        ["Infrastructure.OpenAI"] = ["Application", "Domain"],
        ["Persistence.PostgreSql"] = ["Application", "Domain"],
        ["Web"] = ["Components", "Application", "Infrastructure.OpenAI", "Persistence.PostgreSql", "ServiceDefaults"],
        ["AppHost"] = ["Web"],
        ["ServiceDefaults"] = []
    };

    [Fact]
    public void All_source_projects_are_in_the_solution_and_have_an_explicit_boundary()
    {
        var projectPaths = Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories);
        var solution = XDocument.Load(Path.Combine(RepositoryRoot, "Marmotte.ChatUi.slnx"));
        var solutionProjects = solution.Descendants("Project")
            .Select(project => Path.GetFullPath(project.Attribute("Path")!.Value, RepositoryRoot))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(AllowedReferences.Count, projectPaths.Length);

        foreach (var projectPath in projectPaths)
        {
            Assert.Contains(projectPath, solutionProjects);
            var name = Path.GetFileNameWithoutExtension(projectPath)["Marmotte.ChatUi.".Length..];
            Assert.True(AllowedReferences.ContainsKey(name), $"Define the boundary for {name}.");
        }
    }

    [Theory]
    [InlineData("Domain")]
    [InlineData("Contracts")]
    [InlineData("Application")]
    [InlineData("Components")]
    [InlineData("Infrastructure.OpenAI")]
    [InlineData("Persistence.PostgreSql")]
    [InlineData("Web")]
    [InlineData("AppHost")]
    [InlineData("ServiceDefaults")]
    public void Project_references_follow_the_architecture(string projectName)
    {
        var projectPath = GetProjectPath(projectName);
        var project = XDocument.Load(projectPath);
        var allowedPaths = AllowedReferences[projectName].Select(GetProjectPath).ToHashSet(StringComparer.Ordinal);

        foreach (var reference in project.Descendants("ProjectReference"))
        {
            var referencePath = Path.GetFullPath(reference.Attribute("Include")!.Value, Path.GetDirectoryName(projectPath)!);
            Assert.Contains(referencePath, allowedPaths);
        }
    }

    [Theory]
    [InlineData("Domain")]
    [InlineData("Contracts")]
    [InlineData("Application")]
    public void Application_core_has_no_external_package_or_framework_dependencies(string projectName)
    {
        var project = XDocument.Load(GetProjectPath(projectName));
        Assert.Empty(project.Descendants("FrameworkReference"));

        using var packages = LoadLockFile(projectName);
        foreach (var framework in packages.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dependency in framework.Value.EnumerateObject())
            {
                Assert.Equal("Project", dependency.Value.GetProperty("type").GetString());
            }
        }
    }

    [Fact]
    public void Components_do_not_pull_in_server_adapters_or_server_frameworks()
    {
        var project = XDocument.Load(GetProjectPath("Components"));
        Assert.Empty(project.Descendants("FrameworkReference"));

        using var packages = LoadLockFile("Components");
        string[] allowedPrefixes = ["Microsoft.AspNetCore.Components", "Microsoft.Extensions.", "System."];
        string[] allowedPackages = ["Microsoft.AspNetCore.Authorization", "Microsoft.AspNetCore.Metadata", "Microsoft.JSInterop"];

        foreach (var framework in packages.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dependency in framework.Value.EnumerateObject())
            {
                var allowed = dependency.Name.Equals("Marmotte.ChatUi.Contracts", StringComparison.OrdinalIgnoreCase)
                    || allowedPackages.Contains(dependency.Name, StringComparer.Ordinal)
                    || allowedPrefixes.Any(prefix => dependency.Name.StartsWith(prefix, StringComparison.Ordinal));

                Assert.True(allowed, $"Review the component dependency: {dependency.Name}.");
            }
        }
    }

    [Fact]
    public void Only_the_model_adapter_directly_references_the_OpenAI_package()
    {
        foreach (var projectName in AllowedReferences.Keys)
        {
            var project = XDocument.Load(GetProjectPath(projectName));
            var referencesSdk = project.Descendants("PackageReference")
                .Any(reference => reference.Attribute("Include")?.Value == "OpenAI");
            Assert.Equal(projectName == "Infrastructure.OpenAI", referencesSdk);
        }
    }

    /// <summary>Resolves a source project by its assembly suffix using the repository's established directory layout.</summary>
    private static string GetProjectPath(string name)
    {
        return Path.Combine(RepositoryRoot, "src", $"Marmotte.ChatUi.{name}", $"Marmotte.ChatUi.{name}.csproj");
    }

    /// <summary>Reads a source project's committed dependency graph for architectural checks.</summary>
    /// <returns>A JSON document that the calling test must dispose.</returns>
    private static JsonDocument LoadLockFile(string name)
    {
        var path = Path.Combine(Path.GetDirectoryName(GetProjectPath(name))!, "packages.lock.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Locates the checkout from the test output directory so checks do not depend on the process working directory.</summary>
    /// <exception cref="DirectoryNotFoundException">No ancestor contains the solution file.</exception>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Marmotte.ChatUi.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Run the architecture tests from a repository checkout.");
    }
}
