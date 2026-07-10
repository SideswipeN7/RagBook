using System.Net;
using FluentAssertions;
using RagBook.Modules.Folders.Features.ListFolders;
using Xunit;

namespace RagBook.Api.IntegrationTests.Folders;

/// <summary>
/// Acceptance tests for the folder endpoints against the real host + Dockerized PostgreSQL. Each test
/// uses a fresh session id so its folders are isolated by the global query filter.
/// </summary>
public sealed class FolderEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private FolderApiClient Client(Guid sessionId)
    {
        return new FolderApiClient(factory, sessionId);
    }

    [Fact]
    public async Task Should_PersistHierarchy_When_CreatingRootThenChild()
    {
        // Arrange (AC-1)
        FolderApiClient client = Client(Guid.NewGuid());

        // Act
        var root = await client.CreateAsync("Umowy", parentId: null);
        var child = await client.CreateAsync("2026", root.Id);

        // Assert
        root.Status.Should().Be(HttpStatusCode.Created);
        child.Status.Should().Be(HttpStatusCode.Created);

        IReadOnlyList<FolderNode> folders = await client.ListAsync();
        folders.Should().HaveCount(2);
        folders.Single(f => f.Id == root.Id).Should().BeEquivalentTo(new FolderNode(root.Id!.Value, null, "Umowy", 1));
        folders.Single(f => f.Id == child.Id).Should().BeEquivalentTo(new FolderNode(child.Id!.Value, root.Id, "2026", 2));
    }

    [Fact]
    public async Task Should_RejectChild_When_ParentAtMaxDepth()
    {
        // Arrange (AC-2) — build a chain at the maximum depth of 3.
        FolderApiClient client = Client(Guid.NewGuid());
        var l1 = await client.CreateAsync("L1", null);
        var l2 = await client.CreateAsync("L2", l1.Id);
        var l3 = await client.CreateAsync("L3", l2.Id);

        // Act
        var l4 = await client.CreateAsync("L4", l3.Id);

        // Assert
        l4.Status.Should().Be(HttpStatusCode.BadRequest);
        l4.Code.Should().Be("folder.max_depth_exceeded");
        (await client.ListAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Should_RejectDuplicate_When_SameNameInSameParentCaseInsensitive()
    {
        // Arrange (AC-3)
        FolderApiClient client = Client(Guid.NewGuid());
        await client.CreateAsync("Umowy", null);

        // Act
        var duplicate = await client.CreateAsync("umowy", null);

        // Assert
        duplicate.Status.Should().Be(HttpStatusCode.Conflict);
        duplicate.Code.Should().Be("folder.duplicate_name");
    }

    [Fact]
    public async Task Should_TreatTrimmedAndCaseFoldedNameAsDuplicate()
    {
        // Arrange (spec Edge Cases / SC-006)
        FolderApiClient client = Client(Guid.NewGuid());
        await client.CreateAsync("Umowy", null);

        // Act
        var duplicate = await client.CreateAsync("  umowy ", null);

        // Assert
        duplicate.Status.Should().Be(HttpStatusCode.Conflict);
        duplicate.Code.Should().Be("folder.duplicate_name");
    }

    [Fact]
    public async Task Should_AllowSameNameUnderDifferentParent()
    {
        // Arrange (AC-3)
        FolderApiClient client = Client(Guid.NewGuid());
        var a = await client.CreateAsync("A", null);
        var b = await client.CreateAsync("B", null);
        await client.CreateAsync("Umowy", a.Id);

        // Act — same name, different parent.
        var underB = await client.CreateAsync("Umowy", b.Id);

        // Assert
        underB.Status.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Should_LeaveDescendantsInPlace_When_RenamingNonEmptyFolder()
    {
        // Arrange (AC-4)
        FolderApiClient client = Client(Guid.NewGuid());
        var root = await client.CreateAsync("Umowy", null);
        var child = await client.CreateAsync("2026", root.Id);

        // Act
        var rename = await client.RenameAsync(root.Id!.Value, "Umowy 2026");

        // Assert — the child keeps its parent and depth; only the root's name changed.
        rename.Status.Should().Be(HttpStatusCode.NoContent);
        IReadOnlyList<FolderNode> folders = await client.ListAsync();
        folders.Single(f => f.Id == root.Id).Name.Should().Be("Umowy 2026");
        folders.Single(f => f.Id == child.Id).Should().BeEquivalentTo(new FolderNode(child.Id!.Value, root.Id, "2026", 2));
    }

    [Fact]
    public async Task Should_RejectRename_When_SiblingNameExists()
    {
        // Arrange (AC-3 on rename)
        FolderApiClient client = Client(Guid.NewGuid());
        await client.CreateAsync("Umowy", null);
        var faktury = await client.CreateAsync("Faktury", null);

        // Act
        var rename = await client.RenameAsync(faktury.Id!.Value, "umowy");

        // Assert
        rename.Status.Should().Be(HttpStatusCode.Conflict);
        rename.Code.Should().Be("folder.duplicate_name");
    }

    [Fact]
    public async Task Should_DeleteEmpty_And_BlockNonEmpty()
    {
        // Arrange (AC-5)
        FolderApiClient client = Client(Guid.NewGuid());
        var parent = await client.CreateAsync("Umowy", null);
        var child = await client.CreateAsync("2026", parent.Id);

        // Act & Assert — deleting the non-empty parent is blocked.
        var blocked = await client.DeleteAsync(parent.Id!.Value);
        blocked.Status.Should().Be(HttpStatusCode.Conflict);
        blocked.Code.Should().Be("folder.not_empty");

        // The empty child deletes; then the now-empty parent deletes.
        (await client.DeleteAsync(child.Id!.Value)).Status.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync(parent.Id!.Value)).Status.Should().Be(HttpStatusCode.NoContent);
        (await client.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Should_OrderSiblingsCaseInsensitiveAlphabetically()
    {
        // Arrange (FR-013) — created out of alphabetical order.
        FolderApiClient client = Client(Guid.NewGuid());
        await client.CreateAsync("banan", null);
        await client.CreateAsync("Ananas", null);
        await client.CreateAsync("czekolada", null);

        // Act
        IReadOnlyList<FolderNode> folders = await client.ListAsync();

        // Assert
        folders.Select(f => f.Name).Should().ContainInOrder("Ananas", "banan", "czekolada");
    }

    [Fact]
    public async Task Should_Return404_When_OperatingOnAnotherSessionsFolder()
    {
        // Arrange (FR-010) — a folder owned by session A.
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var created = await Client(sessionA).CreateAsync("Umowy", null);

        // Act — session B tries to rename and delete A's folder.
        var rename = await Client(sessionB).RenameAsync(created.Id!.Value, "Hijack");
        var delete = await Client(sessionB).DeleteAsync(created.Id!.Value);

        // Assert — indistinguishable from a non-existent folder.
        rename.Status.Should().Be(HttpStatusCode.NotFound);
        rename.Code.Should().Be("folder.not_found");
        delete.Status.Should().Be(HttpStatusCode.NotFound);
        delete.Code.Should().Be("folder.not_found");
    }
}
