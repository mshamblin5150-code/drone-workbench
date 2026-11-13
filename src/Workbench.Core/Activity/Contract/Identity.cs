namespace Workbench.Core.Activity.Contract;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

public enum SourceKind
{
    Claude,
    Codex,
    WindowsProcess,
}

public enum WorkKind
{
    Session,
    Turn,
    Subagent,
    Thread,
    Command,
    Job,
}

public enum ReferenceScope
{
    SourceStable,
    ConnectionScoped,
}

public enum OwnershipEvidence
{
    None,
    SameUserBackgroundListing,
    WorkbenchOwnedServer,
}

// Only ActivityLedger.StartGeneration mints a first value, so a restarted adapter cannot start below what the ledger saw and be ignored as older.
public readonly record struct SourceGeneration
{
    internal SourceGeneration(long value) => this.Value = value;

    internal long Value { get; }

    public static bool operator <(SourceGeneration left, SourceGeneration right) => left.Value < right.Value;

    public static bool operator >(SourceGeneration left, SourceGeneration right) => left.Value > right.Value;

    public SourceGeneration Next() => this.Value > 0
        ? new(checked(this.Value + 1))
        : throw new InvalidOperationException("Only the ledger issues a first generation.");

    public override string ToString() => $"g{this.Value}";
}

// A process id alone never identifies work: a reused id with a new creation time is a different incarnation.
public readonly record struct ProcessIncarnation
{
    public ProcessIncarnation(int processId, DateTimeOffset createdAtUtc)
    {
        if (processId <= 0)
        {
            throw Grammar.Invalid(nameof(processId));
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw Grammar.Invalid(nameof(createdAtUtc));
        }

        this.ProcessId = processId;
        this.CreatedAtUtc = createdAtUtc;
    }

    public int ProcessId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public override string ToString() => $"process#{Grammar.Redact($"{this.ProcessId}@{this.CreatedAtUtc.UtcTicks}")}";
}

// The owner text is hashed inside Mint and never kept, so no configuration path can leave through this id.
public sealed record SourceInstanceId
{
    private readonly string fingerprint;

    private SourceInstanceId(SourceKind kind, string fingerprint)
    {
        this.Kind = kind;
        this.fingerprint = fingerprint;
    }

    public SourceKind Kind { get; }

    // For Codex the owner is the server process identity or endpoint, never CODEX_HOME alone, so two servers on one home mint two ids.
    public static SourceInstanceId Mint(SourceKind kind, ReadOnlySpan<byte> fingerprintKey, ReadOnlySpan<char> owner)
    {
        Grammar.RequireDefined(kind, nameof(kind));
        if (fingerprintKey.IsEmpty)
        {
            throw Grammar.Invalid(nameof(fingerprintKey));
        }

        if (owner.IsWhiteSpace())
        {
            throw Grammar.Invalid(nameof(owner));
        }

        var hash = HMACSHA256.HashData(fingerprintKey, Encoding.UTF8.GetBytes($"{kind}\n{owner}"));
        return new(kind, Convert.ToHexStringLower(hash, 0, 16));
    }

    public override string ToString() => $"{this.Kind}#{this.fingerprint[..8]}";
}

// The native id takes part in equality but has no public accessor and is redacted in ToString.
public sealed record WorkKey
{
    private readonly string nativeId;

    private WorkKey(SourceInstanceId source, WorkKind kind, string nativeId, SourceGeneration? connection)
    {
        this.Source = source;
        this.Kind = kind;
        this.nativeId = nativeId;
        this.Connection = connection;
    }

    public SourceInstanceId Source { get; }

    public WorkKind Kind { get; }

    // Set only for commands: a command never keeps its identity across a reconnect.
    public SourceGeneration? Connection { get; }

    internal string NativeId => this.nativeId;

    public static WorkKey Of(SourceInstanceId source, WorkKind kind, string nativeId)
    {
        ArgumentNullException.ThrowIfNull(source);
        Grammar.RequireDefined(kind, nameof(kind));
        if (kind == WorkKind.Command)
        {
            throw new ArgumentException("A command key carries its connection generation.", nameof(kind));
        }

        return new(source, kind, Grammar.RequireNativeId(nativeId, nameof(nativeId)), null);
    }

    public static WorkKey Command(SourceInstanceId source, SourceGeneration connection, string nativeId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (connection == default)
        {
            throw Grammar.Invalid(nameof(connection));
        }

        return new(source, WorkKind.Command, Grammar.RequireNativeId(nativeId, nameof(nativeId)), connection);
    }

    public override string ToString() => $"{this.Kind}#{Grammar.Redact(this.nativeId)}";
}

// Scope derives from the target key, so a connection-scoped reference and its command key cannot disagree about the connection.
public sealed record ControlReference
{
    private readonly string handle;

    private ControlReference(WorkKey target, string handle)
    {
        this.Target = target;
        this.handle = handle;
    }

    public WorkKey Target { get; }

    public ReferenceScope Scope => this.Target.Connection is null ? ReferenceScope.SourceStable : ReferenceScope.ConnectionScoped;

    internal string Handle => this.handle;

    // A short id that is not a prefix of the session id is source drift, not a programmer error, so this returns false.
    public static bool TrySourceStable(WorkKey session, string handle, [NotNullWhen(true)] out ControlReference? reference)
    {
        ArgumentNullException.ThrowIfNull(session);
        reference = session.Kind == WorkKind.Session && Grammar.IsNativeId(handle) && session.NativeId.StartsWith(handle, StringComparison.Ordinal)
            ? new(session, handle)
            : null;
        return reference is not null;
    }

    public static bool TryConnectionScoped(WorkKey command, string handle, [NotNullWhen(true)] out ControlReference? reference)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Kind != WorkKind.Command)
        {
            throw new ArgumentException("A connection-scoped reference targets a command.", nameof(command));
        }

        reference = Grammar.IsNativeId(handle) ? new(command, handle) : null;
        return reference is not null;
    }

    public bool IsUsableAt(SourceGeneration current) => this.Target.Connection is not { } connection || connection == current;

    public override string ToString() => $"ref({this.Scope}) -> {this.Target}";
}

public sealed record ProjectRef
{
    private ProjectRef(string? token) => this.Token = token;

    public static ProjectRef Unassigned { get; } = new((string?)null);

    public string? Token { get; }

    public bool IsAssigned => this.Token is not null;

    // The slug grammar rejects separators, colons and whitespace, so a path can never become a project token.
    public static ProjectRef Of(string token) => new(Grammar.RequireSlug(token, nameof(token)));
}

public sealed record Association
{
    internal Association(ProcessIncarnation incarnation, Channel via, DateTimeOffset linkedAt)
    {
        this.Incarnation = incarnation;
        this.Via = via;
        this.LinkedAt = linkedAt;
    }

    public ProcessIncarnation Incarnation { get; }

    public Channel Via { get; }

    public DateTimeOffset LinkedAt { get; }
}

// Messages name the parameter and never the value, because the value may be a source id, a path or a process id.
internal static class Grammar
{
    private static readonly SearchValues<char> NativeIdCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._-");

    private static readonly SearchValues<char> SlugCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-");

    private static readonly SearchValues<char> VersionCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.+-");

    internal static bool IsNativeId(string? value) => value is { Length: >= 1 and <= 128 } && !value.AsSpan().ContainsAnyExcept(NativeIdCharacters);

    internal static string RequireNativeId(string? value, string parameterName) => IsNativeId(value) ? value! : throw Invalid(parameterName);

    internal static string RequireSlug(string? value, string parameterName) =>
        value is { Length: >= 1 and <= 64 } && value[0] != '-' && !value.AsSpan().ContainsAnyExcept(SlugCharacters)
            ? value
            : throw Invalid(parameterName);

    internal static string RequireVersion(string? value, string parameterName) =>
        value is { Length: >= 1 and <= 64 } && !value.AsSpan().ContainsAnyExcept(VersionCharacters) ? value : throw Invalid(parameterName);

    internal static void RequireDefined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw Invalid(parameterName);
        }
    }

    internal static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw Invalid(parameterName);
        }
    }

    internal static ArgumentException Invalid(string parameterName) => new("The value is outside the contract grammar.", parameterName);

    internal static string Redact(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 4);
}
