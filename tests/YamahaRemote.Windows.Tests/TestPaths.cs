using System.IO;
using System.Reflection;

namespace YamahaRemote.Windows.Tests;

internal static class TestPaths
{
    internal static string ProjectRoot => Metadata("ProjectRoot");

    internal static string ValidationFile(params string[] parts)
    {
        string path = Path.Combine(new[] { Metadata("ProjectWorkspaceRoot"), "validation" }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string Metadata(string key)
    {
        return typeof(TestPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key).Value
            ?? throw new InvalidOperationException($"Missing test assembly metadata: {key}");
    }
}
