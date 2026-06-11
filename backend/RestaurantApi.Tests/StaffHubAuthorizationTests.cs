using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using RestaurantApi.Hubs;
using RestaurantApi.Tenancy;
using Xunit;

namespace RestaurantApi.Tests;

public class StaffHubAuthorizationTests
{
    [Fact]
    public async Task JoinRoleGroup_rejects_restaurant_without_membership()
    {
        var restaurantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        var hub = BuildHub(
            groups,
            userId,
            new FakeTenantAccess((_, _) => Task.FromResult(false)));

        Func<Task> act = () => hub.JoinRoleGroup(restaurantId.ToString(), "admin");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("You do not have access to this restaurant.");
        groups.AddedGroups.Should().BeEmpty();
    }

    [Fact]
    public async Task JoinRoleGroup_rejects_unknown_role_group()
    {
        var restaurantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        var hub = BuildHub(
            groups,
            userId,
            new FakeTenantAccess((_, _) => Task.FromResult(true)));

        Func<Task> act = () => hub.JoinRoleGroup(restaurantId.ToString(), "billing");

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Unknown staff group.");
        groups.AddedGroups.Should().BeEmpty();
    }

    [Fact]
    public async Task JoinRoleGroup_adds_group_when_user_can_access_restaurant()
    {
        var restaurantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        var hub = BuildHub(
            groups,
            userId,
            new FakeTenantAccess((_, _) => Task.FromResult(true)));

        await hub.JoinRoleGroup(restaurantId.ToString(), "admin");

        groups.AddedGroups.Should().Contain(TenantGroups.Role(restaurantId, "admin"));
    }

    [Fact]
    public async Task JoinRoleGroup_canonicalizes_role_names()
    {
        var restaurantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groups = new RecordingGroupManager();
        var hub = BuildHub(
            groups,
            userId,
            new FakeTenantAccess((_, _) => Task.FromResult(true)));

        await hub.JoinRoleGroup(restaurantId.ToString(), "ADMIN");

        groups.AddedGroups.Should().ContainSingle()
            .Which.Should().Be(TenantGroups.Role(restaurantId, "admin"));
    }

    private static StaffHub BuildHub(
        IGroupManager groups,
        Guid userId,
        ITenantAccess tenantAccess)
    {
        return new StaffHub(tenantAccess)
        {
            Context = new TestHubCallerContext(userId),
            Groups = groups,
        };
    }

    private sealed class FakeTenantAccess : ITenantAccess
    {
        private readonly Func<Guid, Guid, Task<bool>> _canAccess;

        public FakeTenantAccess(Func<Guid, Guid, Task<bool>> canAccess)
        {
            _canAccess = canAccess;
        }

        public Task<bool> CanAccessRestaurantAsync(Guid userId, Guid restaurantId)
            => _canAccess(userId, restaurantId);

        public Task<bool> IsOrgOwnerAsync(Guid userId, Guid organizationId)
            => Task.FromResult(false);
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<string> AddedGroups { get; } = new();

        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            AddedGroups.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal _user;

        public TestHubCallerContext(Guid userId)
        {
            _user = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                authenticationType: "Test"));
        }

        public override string ConnectionId => "connection-1";
        public override string? UserIdentifier => _user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User => _user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}
