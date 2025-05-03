using System.IO;
using System.Threading.Tasks;

public static class ResourceHelper
{
    public static async Task ExtractEmbeddedResource(string resourcePath, string outputPath)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = $"BoomBx.{resourcePath.Replace('\\', '.').Replace('/', '.')}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Resource {resourceName} not found");

        using var fileStream = File.Create(outputPath);
        await stream.CopyToAsync(fileStream);
    }
}