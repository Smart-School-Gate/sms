using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SmartSchool.ModuleSelection.Tests;

public sealed class ModulePublishTests
{
    [Fact]
    public async Task Default_and_explicit_false_exclude_homework_and_true_includes_it()
    {
        var root = FindRepositoryRoot();
        var dotnet = FindDotnet();
        var omitted = NewOutputDirectory();
        var foundation = NewOutputDirectory();
        var enabled = NewOutputDirectory();
        try
        {
            var omittedRun = await RunAsync(dotnet, root, "publish", Project(root), output: omitted);
            var foundationRun = await RunAsync(dotnet, root, "publish", Project(root), output: foundation, includeHomework: "false");
            var enabledRun = await RunAsync(dotnet, root, "publish", Project(root), output: enabled, includeHomework: "true");

            Assert.True(omittedRun.ExitCode == 0, omittedRun.Output);
            Assert.True(foundationRun.ExitCode == 0, foundationRun.Output);
            Assert.True(enabledRun.ExitCode == 0, enabledRun.Output);

            AssertExcluded(omitted);
            AssertExcluded(foundation);
            AssertIncluded(enabled);
            Assert.DoesNotContain("SmartSchool.Modules.Homework ->", omittedRun.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("SmartSchool.Modules.Homework ->", foundationRun.Output, StringComparison.Ordinal);
            Assert.Contains("SmartSchool.Modules.Homework ->", enabledRun.Output, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(omitted);
            DeleteDirectory(foundation);
            DeleteDirectory(enabled);
        }
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("TRUE")]
    [InlineData(" true")]
    public async Task Unsupported_include_homework_value_fails(string value)
    {
        var root = FindRepositoryRoot();
        var run = await RunAsync(FindDotnet(), root, "build", Project(root), includeHomework: value);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("IncludeHomework", run.Output, StringComparison.Ordinal);
        Assert.Contains(value, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_module_list_is_rejected()
    {
        var root = FindRepositoryRoot();
        var run = await RunAsync(
            FindDotnet(),
            root,
            "build",
            Project(root),
            includeHomework: "true",
            smartSchoolModules: "Homework");
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("SmartSchoolModules", run.Output, StringComparison.Ordinal);
        Assert.Contains("Homework", run.Output, StringComparison.Ordinal);
    }

    private static void AssertExcluded(string output)
    {
        Assert.False(File.Exists(Path.Combine(output, "SmartSchool.Modules.Homework.dll")));
        var apiReferences = AssemblyReferences(Path.Combine(output, "SmartSchool.Api.dll"));
        Assert.DoesNotContain("SmartSchool.Modules.Homework", apiReferences);
        Assert.Contains("SmartSchool.Core", apiReferences);
        Assert.Contains("SmartSchool.Contracts", apiReferences);
        Assert.Contains("SmartSchool.Infrastructure", apiReferences);
        Assert.DoesNotContain("SmartSchool.Modules.Homework", File.ReadAllText(Path.Combine(output, "SmartSchool.Api.deps.json")));
        Assert.False(ReferencesMethod(Path.Combine(output, "SmartSchool.Api.dll"), "HomeworkModule", "MapHomeworkModule"));
        Assert.DoesNotContain("SmartSchool.Modules.Homework", AssemblyReferences(Path.Combine(output, "SmartSchool.Core.dll")));
        Assert.DoesNotContain("SmartSchool.Modules.Homework", AssemblyReferences(Path.Combine(output, "SmartSchool.Infrastructure.dll")));
    }

    private static void AssertIncluded(string output)
    {
        Assert.True(File.Exists(Path.Combine(output, "SmartSchool.Modules.Homework.dll")));
        var apiReferences = AssemblyReferences(Path.Combine(output, "SmartSchool.Api.dll"));
        Assert.Contains("SmartSchool.Modules.Homework", apiReferences);
        Assert.Contains("SmartSchool.Core", apiReferences);
        Assert.Contains("SmartSchool.Infrastructure", apiReferences);
        Assert.Contains("SmartSchool.Modules.Homework", File.ReadAllText(Path.Combine(output, "SmartSchool.Api.deps.json")));
        Assert.True(ReferencesMethod(Path.Combine(output, "SmartSchool.Api.dll"), "HomeworkModule", "AddHomeworkModule"));
        Assert.True(ReferencesMethod(Path.Combine(output, "SmartSchool.Api.dll"), "HomeworkModule", "MapHomeworkModule"));

        var moduleReferences = AssemblyReferences(Path.Combine(output, "SmartSchool.Modules.Homework.dll"));
        Assert.DoesNotContain("SmartSchool.Api", moduleReferences);
        Assert.DoesNotContain("SmartSchool.Core", moduleReferences);
        Assert.DoesNotContain("SmartSchool.Contracts", moduleReferences);
        Assert.DoesNotContain("SmartSchool.Infrastructure", moduleReferences);
    }

    private static List<string> AssemblyReferences(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new List<string>();
        foreach (var handle in reader.AssemblyReferences)
            names.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
        return names;
    }

    private static bool ReferencesMethod(string path, string typeName, string methodName)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
                continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (reader.GetString(type.Name) == typeName && reader.GetString(member.Name) == methodName)
                return true;
        }

        return false;
    }

    private static async Task<CommandResult> RunAsync(
        string dotnet,
        string root,
        string command,
        string project,
        string? output = null,
        string? includeHomework = null,
        string? smartSchoolModules = null)
    {
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("--nologo");
        if (includeHomework is not null)
            start.ArgumentList.Add($"-p:IncludeHomework={includeHomework}");
        if (smartSchoolModules is not null)
            start.ArgumentList.Add($"-p:SmartSchoolModules={smartSchoolModules}");
        if (output is not null)
        {
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(output);
        }

        var dotnetDirectory = Path.GetDirectoryName(dotnet)!;
        start.Environment["DOTNET_ROOT"] = dotnetDirectory;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["PATH"] = dotnetDirectory + Path.PathSeparator + start.Environment["PATH"];
        start.Environment.Remove("DATABASE_URL");
        start.Environment.Remove("MIGRATION_DATABASE_URL");
        start.Environment.Remove("EXPECTED_DATABASE_NAME");
        start.Environment.Remove("IncludeHomework");
        start.Environment.Remove("SmartSchoolModules");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await process.WaitForExitAsync(timeout.Token);
        return new CommandResult(process.ExitCode, (await stdout) + (await stderr));
    }

    private static string Project(string root) => Path.Combine(root, "src", "SmartSchool.Api", "SmartSchool.Api.csproj");

    private static string NewOutputDirectory() => Path.Combine(Path.GetTempPath(), "smart-school-publish-" + Guid.NewGuid().ToString("N"));

    private static string FindDotnet()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "dotnet",
            "dotnet.exe");
        if (File.Exists(local))
            return local;

        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return candidate;
        }

        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartSchool.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("SmartSchool.slnx was not found.");
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record CommandResult(int ExitCode, string Output);
}
