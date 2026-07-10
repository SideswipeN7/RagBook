using FluentAssertions;
using RagBook.Modules.Documents.Domain;
using Xunit;

namespace RagBook.Domain.Tests.Documents;

public sealed class FileNameTests
{
    [Fact]
    public void Should_SplitBaseAndExtension()
    {
        // Arrange & Act
        FileName name = FileName.Parse("umowa.pdf");

        // Assert
        name.Base.Should().Be("umowa");
        name.Extension.Should().Be(".pdf");
        name.Value.Should().Be("umowa.pdf");
    }

    [Fact]
    public void Should_ProduceNumberedSuffixFromOne_When_Deduplicating()
    {
        // Arrange
        FileName name = FileName.Parse("umowa.pdf");

        // Act & Assert (AC-5, clarify Q3 — start at 1)
        name.WithSuffix(1).Should().Be("umowa (1).pdf");
        name.WithSuffix(2).Should().Be("umowa (2).pdf");
    }

    [Fact]
    public void Should_HandleNameWithoutExtension()
    {
        // Arrange & Act
        FileName name = FileName.Parse("README");

        // Assert
        name.Base.Should().Be("README");
        name.Extension.Should().BeEmpty();
        name.WithSuffix(1).Should().Be("README (1)");
    }

    [Fact]
    public void Should_TreatLeadingDotAsBase()
    {
        // Arrange & Act — a dotfile has no extension.
        FileName name = FileName.Parse(".gitignore");

        // Assert
        name.Base.Should().Be(".gitignore");
        name.Extension.Should().BeEmpty();
    }
}
