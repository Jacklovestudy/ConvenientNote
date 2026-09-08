using System.Xml.Linq;
using Xunit;

namespace ConvenientNote.Architecture.Tests;

/// <summary>Checks the real project graph, including references introduced with arbitrary aliases.</summary>
public sealed class ModuleBoundaryTests
{
    private static readonly string Root = FindRoot();
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    [Theory]
    [InlineData("Todos")]
    [InlineData("Notes")]
    [InlineData("Calendar")]
    [InlineData("ColorPicker")]
    public void Each_module_has_exactly_five_layer_projects(string module)
    {
        string[] layers = ["Application", "Contracts", "Domain", "Infrastructure", "UI"];
        var projects = LoadProjects().Where(p => p.Module == module).ToArray();
        Assert.Equal(layers, projects.Select(p => p.Layer).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var project in projects)
            Assert.Equal($"ConvenientNote.{module}.{project.Layer}", project.Name);
    }

    [Fact]
    public void Domain_projects_have_no_framework_or_outward_dependencies()
    {
        foreach (var project in LoadProjects().Where(p => p.Module is not null && p.Layer == "Domain"))
        {
            Assert.False(project.Elements("UseWPF").Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase)), project.Name);
            Assert.False(project.Elements("UseWindowsForms").Any(e => e.Value.Equals("true", StringComparison.OrdinalIgnoreCase)), project.Name);
            var packagesAndAssemblies = project.Elements("PackageReference", "FrameworkReference", "Reference")
                .Select(e => e.Attribute("Include")?.Value ?? "");
            foreach (var reference in packagesAndAssemblies)
                Assert.False(new[] { "Prism", "EntityFramework", "Sqlite", "WindowsDesktop", "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows", "WPF" }
                    .Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)), $"{project.Name} depends on {reference}");
            foreach (var path in project.References)
            {
                var target = ReadProject(path);
                Assert.True(target.Module is null || (target.Module == project.Module && target.Layer == "Domain"),
                    $"Domain cannot depend on {target.Name}: {project.Name}");
                Assert.False(target.Layer is "UI" or "Infrastructure" or "Application" or "Contracts", $"Domain outward dependency: {target.Name}");
            }
        }
    }

    [Fact]
    public void Modules_never_reference_host_or_compatibility_projects()
    {
        var graph = LoadProjects().ToDictionary(p => p.Path, Paths);
        foreach (var project in LoadProjects().Where(p => p.Module is not null))
        foreach (var path in Reachable(project, graph))
        {
            var relative = Relative(path);
            Assert.False(relative.StartsWith("UI/ConvenientNote.Desktop/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("src/Compatibility/", StringComparison.OrdinalIgnoreCase)
                || Paths.Equals(path, Path.Combine(Root, "ConvenientNote.csproj")),
                $"{project.Name} references forbidden project {relative}");
        }
    }

    [Fact]
    public void Cross_module_references_target_only_contracts()
    {
        foreach (var project in LoadProjects().Where(p => p.Module is not null))
        foreach (var path in project.References)
        {
            var target = ReadProject(path);
            if (target.Module is not null && target.Module != project.Module)
                Assert.True(target.Layer == "Contracts", $"{project.Name} bypasses contracts with {target.Name}");
        }
    }

    [Fact]
    public void UI_has_no_infrastructure_reference_and_contracts_expose_no_domain_reference()
    {
        var graph = LoadProjects().ToDictionary(p => p.Path, Paths);
        foreach (var project in LoadProjects().Where(p => p.Module is not null))
        foreach (var path in Reachable(project, graph))
        {
            var target = ReadProject(path);
            if (project.Layer == "UI") Assert.NotEqual("Infrastructure", target.Layer);
            if (project.Layer == "Contracts") Assert.NotEqual("Domain", target.Layer);
        }
    }

    [Fact]
    public void All_production_project_references_exist()
    {
        foreach (var project in LoadProjects())
        foreach (var reference in project.References)
            Assert.True(File.Exists(reference), $"{Relative(project.Path)} references missing {Relative(reference)}");
    }

    [Fact]
    public void Production_project_graph_has_no_cycles()
    {
        var graph = LoadProjects().ToDictionary(p => p.Path, Paths);
        var visited = new HashSet<string>(Paths);
        var active = new HashSet<string>(Paths);
        var stack = new List<string>();
        foreach (var path in graph.Keys) Visit(path);

        void Visit(string path)
        {
            Assert.False(active.Contains(path), "Project reference cycle: " + string.Join(" → ", stack.Append(path).Select(Relative)));
            if (!visited.Add(path)) return;
            active.Add(path); stack.Add(path);
            if (graph.TryGetValue(path, out var project))
                foreach (var reference in project.References) Visit(reference);
            stack.RemoveAt(stack.Count - 1); active.Remove(path);
        }
    }

    private static Project[] LoadProjects()
    {
        var paths = new[] { "src", "UI" }.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(Root, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !Relative(path).Split('/').Any(part => part is "obj" or "bin"));
        var legacyHost = Path.Combine(Root, "ConvenientNote.csproj");
        if (File.Exists(legacyHost)) paths = paths.Append(legacyHost);
        return paths.Select(ReadProject).ToArray();
    }

    private static IEnumerable<string> Reachable(Project source, IReadOnlyDictionary<string, Project> graph)
    {
        var visited = new HashSet<string>(Paths);
        var pending = new Stack<string>(source.References);
        while (pending.TryPop(out var path))
        {
            if (!visited.Add(path)) continue;
            yield return path;
            if (graph.TryGetValue(path, out var project))
                foreach (var reference in project.References) pending.Push(reference);
        }
    }

    private static Project ReadProject(string path)
    {
        Assert.True(File.Exists(path), "Missing project: " + Relative(path));
        var document = XDocument.Load(path);
        var relative = Relative(path).Split('/');
        var name = Path.GetFileNameWithoutExtension(path);
        var segments = name.Split('.');
        string? module = segments.Length == 3 && new[] { "Notes", "Todos", "Calendar", "ColorPicker" }.Contains(segments[1]) ? segments[1] : null;
        var references = document.Descendants().Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, value!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToArray();
        return new Project(Path.GetFullPath(path), name, module, name.Split('.')[^1], document, references);
    }

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ConvenientNote.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate repository root containing ConvenientNote.slnx.");
    }

    private sealed record Project(string Path, string Name, string? Module, string Layer, XDocument Document, string[] References)
    {
        public IEnumerable<XElement> Elements(params string[] names) => Document.Descendants().Where(e => names.Contains(e.Name.LocalName));
    }
}
