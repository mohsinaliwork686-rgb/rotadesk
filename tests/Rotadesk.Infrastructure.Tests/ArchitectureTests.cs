namespace Rotadesk.Infrastructure.Tests;

/// <summary>
/// Checks on the source itself. Only the outbox works across businesses at run time: nothing calls
/// IgnoreQueryFilters, and the system context is used only by the outbox processor, the migrations and dotnet ef.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] AllowedSystemTenantFiles =
    [
        "Tenancy.cs",
        "OutboxProcessor.cs",
        "DependencyInjection.cs",
        "DesignTimeDbContextFactory.cs",
    ];

    [Fact]
    public void No_code_turns_the_tenant_filters_off()
    {
        Assert.Empty(SourceFiles().Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters", StringComparison.Ordinal)).Select(Path.GetFileName));
    }

    [Fact]
    public void Only_the_outbox_the_migrations_and_dotnet_ef_use_the_system_context()
    {
        var users = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("SystemTenant", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f)!)
            .Where(name => !AllowedSystemTenantFiles.Contains(name));

        Assert.Empty(users);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Rotadesk.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
