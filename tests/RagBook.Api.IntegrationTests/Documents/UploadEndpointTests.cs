using System.Net;
using System.Text;
using FluentAssertions;
using RagBook.Api.IntegrationTests.Folders;
using Xunit;

namespace RagBook.Api.IntegrationTests.Documents;

/// <summary>
/// Acceptance tests for <c>POST /api/documents</c> against the real host + Dockerized PostgreSQL + a
/// per-factory local blob root. Each test uses a fresh session id (isolated by the query filter).
/// </summary>
public sealed class UploadEndpointTests(RagBookApiFactory factory) : IClassFixture<RagBookApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nhello world\n");

    private DocumentApiClient Documents(Guid sessionId) => new(factory, sessionId);

    private FolderApiClient Folders(Guid sessionId) => new(factory, sessionId);

    [Fact]
    public async Task Should_UploadPdfIntoFolder_When_Valid()
    {
        // Arrange (AC-1/AC-4)
        var sessionId = Guid.NewGuid();
        var folder = await Folders(sessionId).CreateAsync("Umowy", null);

        // Act
        var upload = await Documents(sessionId).UploadAsync(Pdf, "umowa.pdf", "application/pdf", folder.Id);

        // Assert
        upload.Status.Should().Be(HttpStatusCode.Created);
        upload.Document!.Status.Should().Be("Processing");
        upload.Document.ContentType.Should().Be("application/pdf");
        upload.Document.FolderId.Should().Be(folder.Id);
    }

    [Fact]
    public async Task Should_PlaceAtRoot_When_NoFolder()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var upload = await Documents(sessionId).UploadAsync(Pdf, "umowa.pdf", "application/pdf", folderId: null);

        // Assert
        upload.Status.Should().Be(HttpStatusCode.Created);
        upload.Document!.FolderId.Should().BeNull();
    }

    [Fact]
    public async Task Should_Reject_When_SignatureMismatch()
    {
        // Arrange (AC-2) — an executable renamed .pdf.
        var sessionId = Guid.NewGuid();
        byte[] exe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];

        // Act
        var upload = await Documents(sessionId).UploadAsync(exe, "malware.pdf", "application/pdf", folderId: null);

        // Assert — rejected, nothing stored.
        upload.Status.Should().Be(HttpStatusCode.BadRequest);
        upload.Code.Should().Be("document.unsupported_file_type");
        factory.CountStoredBlobs(sessionId).Should().Be(0);
    }

    [Fact]
    public async Task Should_RejectEmpty_ServerSide()
    {
        // Arrange (FR-004)
        var sessionId = Guid.NewGuid();

        // Act
        var upload = await Documents(sessionId).UploadAsync([], "empty.txt", "text/plain", folderId: null);

        // Assert
        upload.Status.Should().Be(HttpStatusCode.BadRequest);
        upload.Code.Should().Be("document.empty_file");
    }

    [Fact]
    public async Task Should_AutoSuffix_When_DuplicateNameInFolder()
    {
        // Arrange (AC-5)
        var sessionId = Guid.NewGuid();
        var folder = await Folders(sessionId).CreateAsync("Umowy", null);

        // Act
        var first = await Documents(sessionId).UploadAsync(Pdf, "umowa.pdf", "application/pdf", folder.Id);
        var second = await Documents(sessionId).UploadAsync(Pdf, "umowa.pdf", "application/pdf", folder.Id);

        // Assert — the second is suffixed from (1); the first keeps its name.
        first.Document!.FileName.Should().Be("umowa.pdf");
        second.Document!.FileName.Should().Be("umowa (1).pdf");
    }

    [Fact]
    public async Task Should_ScopeSuffixPerFolder_When_SameNameDifferentFolders()
    {
        // Arrange (FR-008)
        var sessionId = Guid.NewGuid();
        var a = await Folders(sessionId).CreateAsync("A", null);
        var b = await Folders(sessionId).CreateAsync("B", null);

        // Act
        var inA = await Documents(sessionId).UploadAsync(Pdf, "umowa.pdf", "application/pdf", a.Id);
        var inB = await Documents(sessionId).UploadAsync(Pdf, "umowa.pdf", "application/pdf", b.Id);

        // Assert — same name in different folders, both unsuffixed.
        inA.Document!.FileName.Should().Be("umowa.pdf");
        inB.Document!.FileName.Should().Be("umowa.pdf");
    }

    [Fact]
    public async Task Should_RejectUpload_And_LeaveNoOrphan_When_QuotaFull()
    {
        // Arrange (FR-007/FR-012) — fill to the 10-document limit.
        var sessionId = Guid.NewGuid();
        for (int i = 0; i < 10; i++)
        {
            var ok = await Documents(sessionId).UploadAsync(Pdf, $"doc{i}.pdf", "application/pdf", folderId: null);
            ok.Status.Should().Be(HttpStatusCode.Created);
        }

        // Act — the 11th is rejected.
        var rejected = await Documents(sessionId).UploadAsync(Pdf, "overflow.pdf", "application/pdf", folderId: null);

        // Assert — quota code, and the rejected upload left no orphan blob (only the 10 admitted remain).
        rejected.Status.Should().Be(HttpStatusCode.Conflict);
        rejected.Code.Should().Be("quota.exceeded");
        factory.CountStoredBlobs(sessionId).Should().Be(10);
    }

    [Fact]
    public async Task Should_Return404_When_TargetFolderInAnotherSession()
    {
        // Arrange (FR-006)
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var folder = await Folders(owner).CreateAsync("Umowy", null);

        // Act
        var upload = await Documents(intruder).UploadAsync(Pdf, "umowa.pdf", "application/pdf", folder.Id);

        // Assert
        upload.Status.Should().Be(HttpStatusCode.NotFound);
        upload.Code.Should().Be("folder.not_found");
        factory.CountStoredBlobs(intruder).Should().Be(0);
    }
}
