namespace Workbench.Core;

using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

public static class LocalChannel
{
    public const int SchemaVersion = 1;

    public const int RequestByteLimit = 256;

    public const string SourcesRequest = """{"schema":1,"request":"sources"}""";

    public const string UnsupportedRequestResponse = """{"schema":1,"error":"UnsupportedRequest"}""";

    public const string SnapshotUnavailableResponse = """{"schema":1,"error":"SnapshotUnavailable"}""";

    public const string InstanceVariable = "DRONE_WORKBENCH_INSTANCE";

    private const string Prefix = "drone-workbench-v1-";

    public static string PipeNameForCurrentUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The local channel requires Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new InvalidOperationException("The current Windows user has no SID.");
        return PipeName(sid.Value, Environment.GetEnvironmentVariable(InstanceVariable));
    }

    public static string PipeName(string userSid, string? instance)
    {
        ArgumentException.ThrowIfNullOrEmpty(userSid);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userSid)))[..16];
        if (string.IsNullOrEmpty(instance))
        {
            return Prefix + hash;
        }

        if (instance.Length > 16 || !instance.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9')))
        {
            throw new ArgumentException(InstanceVariable + " must be 1 to 16 lowercase letters or digits.", nameof(instance));
        }

        return Prefix + hash + "-" + instance;
    }
}
