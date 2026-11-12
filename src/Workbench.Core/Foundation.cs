namespace Workbench.Core;

public static class Foundation
{
    public static string Version => typeof(Foundation).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static string Stage => "foundation";
}
