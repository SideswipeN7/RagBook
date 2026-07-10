using System.Net;
using System.Text;
using FluentAssertions;
using RagBook.Api.IntegrationTests.Documents;
using Xunit;

namespace RagBook.Api.IntegrationTests.Folders;

/// <summary>
/// Closes US-09 AC-5 end-to-end (US-04 FR-014): once a folder contains an uploaded document, deleting it
/// is blocked with <c>folder.not_empty</c> — the real <c>DocumentFolderFileProbe</c> now backs the
/// folder-emptiness file arm that shipped as a no-op seam in US-09.
/// </summary>
public sealed class FolderDeleteBlockedByFileTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nx\n");

    [Fact]
    public async Task Should_BlockFolderDelete_When_FolderHasFile()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var folders = new FolderApiClient(factory, sessionId);
        var folder = await folders.CreateAsync("Umowy", null);
        var upload = await new DocumentApiClient(factory, sessionId)
            .UploadAsync(Pdf, "umowa.pdf", "application/pdf", folder.Id);
        upload.Status.Should().Be(HttpStatusCode.Created);

        // Act
        var delete = await folders.DeleteAsync(folder.Id!.Value);

        // Assert — the folder is not empty of files, so the delete is refused.
        delete.Status.Should().Be(HttpStatusCode.Conflict);
        delete.Code.Should().Be("folder.not_empty");
    }
}
