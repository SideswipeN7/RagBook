using System.Text;
using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Shared.Results;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

public sealed class FileTypeDetectorTests
{
    [Fact]
    public void Should_DetectPdf_When_PdfSignature()
    {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ\n");

        // Act
        Result<SupportedFileType> result = FileTypeDetector.Detect(content, "umowa.pdf");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(SupportedFileType.Pdf);
    }

    [Fact]
    public void Should_RejectExeRenamedPdf_When_Detecting()
    {
        // Arrange (AC-2) — an MZ executable header with NULs; no %PDF- and not valid text.
        byte[] content = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

        // Act
        Result<SupportedFileType> result = FileTypeDetector.Detect(content, "malware.pdf");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DocumentErrors.UnsupportedFileType);
    }

    [Fact]
    public void Should_ClassifyMarkdown_When_TextWithMdExtension()
    {
        // Arrange
        byte[] content = Encoding.UTF8.GetBytes("# Tytuł\n\nTreść.");

        // Act
        Result<SupportedFileType> result = FileTypeDetector.Detect(content, "notes.md");

        // Assert
        result.Value.Should().Be(SupportedFileType.Markdown);
    }

    [Fact]
    public void Should_ClassifyPlainText_When_TextWithoutMdExtension()
    {
        // Arrange
        byte[] content = Encoding.UTF8.GetBytes("zwykły tekst");

        // Act
        Result<SupportedFileType> result = FileTypeDetector.Detect(content, "notes.txt");

        // Assert
        result.Value.Should().Be(SupportedFileType.PlainText);
    }

    [Fact]
    public void Should_RejectBinary_When_TextExtensionButNotUtf8Text()
    {
        // Arrange — control/NUL bytes make it non-text despite the .txt name.
        byte[] content = [0x00, 0x01, 0x02, 0xFF, 0xFE];

        // Act
        Result<SupportedFileType> result = FileTypeDetector.Detect(content, "fake.txt");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DocumentErrors.UnsupportedFileType);
    }
}
