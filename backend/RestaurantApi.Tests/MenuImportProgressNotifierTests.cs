using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Hubs;
using RestaurantApi.Services;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// Unit tests for MenuImportProgressNotifier throttling logic.
/// Uses a counting stub of IHubContext so we can verify exactly how many
/// SignalR sends were made.
/// </summary>
public class MenuImportProgressNotifierTests
{
    private static (MenuImportProgressNotifier notifier, CountingHubContext hub) Build()
    {
        var hub = new CountingHubContext();
        var notifier = new MenuImportProgressNotifier(hub);
        return (notifier, hub);
    }

    // ── Test 1: Throttle — two quick calls only send once ─────────────────────
    [Fact]
    public async Task EmitProgressAsync_two_immediate_calls_only_sends_one_event()
    {
        var (notifier, hub) = Build();
        var jobId = Guid.NewGuid();
        var ridA = Guid.NewGuid();

        var payload = new { processed = 1, total = 10 };

        // Both calls happen without any delay between them
        await notifier.EmitProgressAsync(ridA, jobId, payload, CancellationToken.None);
        await notifier.EmitProgressAsync(ridA, jobId, payload, CancellationToken.None);

        hub.ProgressCount.Should().Be(1,
            "second call within 250 ms throttle window must be suppressed");
    }

    // ── Test 2: EmitCompletedAsync is never throttled ─────────────────────────
    [Fact]
    public async Task EmitCompletedAsync_is_never_throttled()
    {
        var (notifier, hub) = Build();
        var jobId = Guid.NewGuid();
        var ridA = Guid.NewGuid();

        var payload = new { status = "complete" };

        await notifier.EmitCompletedAsync(ridA, jobId, payload, CancellationToken.None);
        await notifier.EmitCompletedAsync(ridA, jobId, payload, CancellationToken.None);

        hub.CompletedCount.Should().Be(2,
            "completed events are always emitted regardless of throttle window");
    }

    // ── Test 3: Different jobIds don't share throttle window ──────────────────
    [Fact]
    public async Task EmitProgressAsync_different_jobIds_have_independent_throttle_windows()
    {
        var (notifier, hub) = Build();
        var jobId1 = Guid.NewGuid();
        var jobId2 = Guid.NewGuid();
        var ridA = Guid.NewGuid();

        var payload = new { processed = 1, total = 10 };

        await notifier.EmitProgressAsync(ridA, jobId1, payload, CancellationToken.None);
        await notifier.EmitProgressAsync(ridA, jobId2, payload, CancellationToken.None);

        hub.ProgressCount.Should().Be(2,
            "each jobId has its own throttle window — both should emit");
    }

    // ── Test 4: Progress is re-emitted after throttle window expires ──────────
    [Fact]
    public async Task EmitProgressAsync_emits_again_after_250ms_throttle_window_expires()
    {
        var (notifier, hub) = Build();
        var jobId = Guid.NewGuid();
        var ridA = Guid.NewGuid();
        var payload = new { processed = 5, total = 10 };

        await notifier.EmitProgressAsync(ridA, jobId, payload, CancellationToken.None);
        // Wait longer than the throttle window
        await Task.Delay(300);
        await notifier.EmitProgressAsync(ridA, jobId, payload, CancellationToken.None);

        hub.ProgressCount.Should().Be(2,
            "after throttle window expires, the next call should emit");
    }

    // ── Test 5: EmitFailedAsync is never throttled ────────────────────────────
    [Fact]
    public async Task EmitFailedAsync_is_never_throttled()
    {
        var (notifier, hub) = Build();
        var jobId = Guid.NewGuid();
        var ridA = Guid.NewGuid();
        var payload = new { error = "something went wrong" };

        await notifier.EmitFailedAsync(ridA, jobId, payload, CancellationToken.None);
        await notifier.EmitFailedAsync(ridA, jobId, payload, CancellationToken.None);

        hub.FailedCount.Should().Be(2,
            "failed events are always emitted regardless of throttle window");
    }

    // ── Stub ──────────────────────────────────────────────────────────────────

    public sealed class CountingHubContext : IHubContext<StaffHub>
    {
        public int ProgressCount;
        public int CompletedCount;
        public int FailedCount;

        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = new FakeGroupManager();

        public CountingHubContext()
        {
            Clients = new CountingHubClients(this);
        }

        private sealed class CountingHubClients : IHubClients
        {
            private readonly CountingHubContext _ctx;
            public CountingHubClients(CountingHubContext ctx) => _ctx = ctx;

            public IClientProxy All => new CountingProxy(_ctx, null);
            public IClientProxy AllExcept(IReadOnlyList<string> ex) => new CountingProxy(_ctx, null);
            public IClientProxy Client(string id) => new CountingProxy(_ctx, null);
            public IClientProxy Clients(IReadOnlyList<string> ids) => new CountingProxy(_ctx, null);
            public IClientProxy Group(string groupName) => new CountingProxy(_ctx, null);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new CountingProxy(_ctx, null);
            public IClientProxy Groups(IReadOnlyList<string> groups) => new CountingProxy(_ctx, null);
            public IClientProxy User(string userId) => new CountingProxy(_ctx, null);
            public IClientProxy Users(IReadOnlyList<string> userIds) => new CountingProxy(_ctx, null);
        }

        private sealed class CountingProxy : IClientProxy
        {
            private readonly CountingHubContext _ctx;
            private readonly string? _groupOverride;

            public CountingProxy(CountingHubContext ctx, string? group)
            {
                _ctx = ctx;
                _groupOverride = group;
            }

            public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
            {
                if (method == "MenuImportProgress") Interlocked.Increment(ref _ctx.ProgressCount);
                else if (method == "MenuImportCompleted") Interlocked.Increment(ref _ctx.CompletedCount);
                else if (method == "MenuImportFailed") Interlocked.Increment(ref _ctx.FailedCount);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
