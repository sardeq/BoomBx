using System.Reflection;

public static class AppVersionHelper
{
    public static string GetInformationalVersion()
    {
        return Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";
    }
}