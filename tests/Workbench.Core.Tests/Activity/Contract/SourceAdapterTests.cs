namespace Workbench.Core.Tests.Activity.Contract;

using System.Reflection;
using System.Runtime.CompilerServices;
using Workbench.Core.Activity.Contract;
using Xunit;
using static Workbench.Core.Tests.Activity.Contract.ContractFixture;

public sealed class SourceAdapterTests
{
    private static readonly Assembly Core = typeof(ActivityLedger).Assembly;

    [Fact]
    public async Task AnObservationOnlyAdapterFeedsTheLedgerAndHasNoControlRoute()
    {
        var clock = Clock();
        var ledger = new ActivityLedger(Settings, clock);
        ISourceAdapter adapter = new IdleSessionAdapter(ledger.StartGeneration(Claude));
        var cancellation = TestContext.Current.CancellationToken;

        await foreach (var batch in adapter.ObserveAsync(cancellation))
        {
            Assert.Equal(ApplyOutcome.Applied, ledger.Apply(batch));
        }

        Assert.Equal(WorkState.Idle, ledger.Find(Session(SessionA))!.State);
        var reference = new ControlReceiptRef(new RequestId(Guid.NewGuid()), ControlAction.StopBackgroundSession, Session(SessionA), default, null, Start);
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.ReconcileAsync(reference, cancellation));
    }

    [Fact]
    public void OnlyCoreCanBuildAuthorityReceiptsViewsOrNewBatchShapes()
    {
        Type[] activityOnly = [typeof(VerifiedControl), typeof(ControlReceipt), typeof(ControlReceiptRef), typeof(WorkView), typeof(SourceView), typeof(Association)];
        Type[] closed = [typeof(SafeSourceBatch), typeof(ListedWork), typeof(HookEvent)];

        Assert.All(activityOnly.Concat(closed), type => Assert.DoesNotContain(
            type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly));
        Assert.All(closed, type => Assert.True(type.IsAbstract && type.GetNestedTypes().Length == 0));
    }

    [Fact]
    public void NoBatchShapeCanCarryAWorkStateRawTextOrAuthority()
    {
        Type[] forbidden = [typeof(WorkState), typeof(ReasonCode), typeof(string), typeof(Exception), typeof(object), typeof(VerifiedControl), typeof(ControlReceipt)];
        var shapes = new[] { typeof(SafeSourceBatch), typeof(ListedWork), typeof(HookEvent) }
            .SelectMany(type => type.GetNestedTypes(BindingFlags.NonPublic).Append(type));

        var carried = shapes.SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
            .SelectMany(type => type.IsGenericType ? type.GetGenericArguments().Append(type) : [type])
            .ToHashSet();

        Assert.NotEmpty(carried);
        Assert.Empty(carried.Intersect(forbidden));
    }

    [Fact]
    public void CoreGrantsItsInternalsOnlyToCoreTests()
    {
        var grantees = Core.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName);

        Assert.Equal(new[] { "Workbench.Core.Tests" }, grantees);
    }

    [Fact]
    public void TheContractReadsTimeOnlyThroughTheInjectedClock()
    {
        string[] ambient = ["DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider.System", "Environment.TickCount", "Stopwatch"];
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "drone-workbench.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Run the tests from inside the repository.");
        }

        var sources = Directory.GetFiles(Path.Combine(root, "src", "Workbench.Core", "Activity", "Contract"), "*.cs");

        Assert.NotEmpty(sources);
        Assert.All(sources, file => Assert.DoesNotContain(ambient, token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal)));
    }

    private sealed class IdleSessionAdapter(SourceGeneration generation) : ISourceAdapter
    {
        public SourceKind Kind => SourceKind.Claude;

        public Task<CapabilityReport> DescribeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CapabilityReportTests.ClaudeReport(generation, "2.1.312"));

        public async IAsyncEnumerable<SafeSourceBatch> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var live = Process(101);
            await Task.Yield();
            yield return SafeSourceBatch.Polled(
                Claude, generation, Start, [ListedWork.Entry(Session(SessionA), Entrypoint.Background, ListedStatus.Idle, incarnation: live)], [live], true);
        }
    }
}
