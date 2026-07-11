using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Features.UploadDocument;
using RagBook.Modules.Documents.Processing;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Application.Tests.Documents;

public sealed class ProcessDocumentHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly DateTimeOffset UploadedAt = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly IDocumentProcessingReader _reader = Substitute.For<IDocumentProcessingReader>();
    private readonly ISessionInitializer _sessionInitializer = Substitute.For<ISessionInitializer>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly ITextExtractor _extractor = Substitute.For<ITextExtractor>();
    private readonly IChunker _chunker = Substitute.For<IChunker>();
    private readonly IChunkRepository _chunkRepository = Substitute.For<IChunkRepository>();
    private readonly IDocumentStatusNotifier _notifier = Substitute.For<IDocumentStatusNotifier>();
    private EmbeddingOptions _embedding = new() { Dimension = 8, BatchSize = 64, RetryCount = 3 };

    private Document GivenExistingDocument()
    {
        Document document = Document.CreateUpload(1_000, "umowa.pdf", "application/pdf", null, "blob", UploadedAt).Value;
        _reader.GetTargetAsync(document.Id, Arg.Any<CancellationToken>())
            .Returns(new ProcessingTarget(SessionId, "blob", "application/pdf"));
        _reader.LoadTrackedAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _fileStorage.OpenReadAsync("blob", Arg.Any<CancellationToken>()).Returns(new MemoryStream([1, 2, 3]));
        _extractor.CanExtract(Arg.Any<string>()).Returns(true);
        _extractor.ExtractAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new ExtractedText([new ExtractedSegment(null, "content")]));

        return document;
    }

    private ProcessDocumentHandler CreateSut(IEmbeddingProvider provider)
    {
        return new ProcessDocumentHandler(
            _reader,
            _sessionInitializer,
            _fileStorage,
            [_extractor],
            _chunker,
            provider,
            _chunkRepository,
            _notifier,
            Options.Create(_embedding));
    }

    [Fact]
    public async Task Should_ExtractChunkEmbedAndMarkReady_When_Readable()
    {
        // Arrange (AC-1)
        Document document = GivenExistingDocument();
        _chunker.Chunk(Arg.Any<IReadOnlyList<ExtractedSegment>>())
            .Returns([new TextChunk(0, "a", null), new TextChunk(1, "b", 2)]);
        var provider = new ScriptedEmbeddingProvider();
        var sut = CreateSut(provider);

        // Act
        await sut.Handle(new DocumentUploaded(document.Id), CancellationToken.None);

        // Assert — the document is ready with the chunk count; chunks saved; status published.
        _sessionInitializer.Received(1).Initialize(SessionId);
        document.Status.Should().Be(DocumentStatus.Ready);
        document.ChunkCount.Should().Be(2);
        await _chunkRepository.Received(1).ReplaceForDocumentAsync(
            document,
            Arg.Is<IReadOnlyList<Chunk>>(c => c.Count == 2),
            Arg.Any<CancellationToken>());
        _notifier.Received(1).Publish(SessionId, Arg.Is<DocumentStatusUpdate>(u => u.Status == "Ready" && u.ChunkCount == 2));
    }

    [Fact]
    public async Task Should_StopQuietly_When_DocumentDeleted()
    {
        // Arrange (AC-4) — the target read returns null (deleted mid-processing).
        var id = Guid.NewGuid();
        _reader.GetTargetAsync(id, Arg.Any<CancellationToken>()).Returns((ProcessingTarget?)null);
        var sut = CreateSut(new ScriptedEmbeddingProvider());

        // Act
        await sut.Handle(new DocumentUploaded(id), CancellationToken.None);

        // Assert — nothing happened.
        _sessionInitializer.DidNotReceive().Initialize(Arg.Any<Guid>());
        await _chunkRepository.DidNotReceive().ReplaceForDocumentAsync(Arg.Any<Document>(), Arg.Any<IReadOnlyList<Chunk>>(), Arg.Any<CancellationToken>());
        _notifier.DidNotReceive().Publish(Arg.Any<Guid>(), Arg.Any<DocumentStatusUpdate>());
    }

    [Fact]
    public async Task Should_MarkFailed_When_NoExtractableText()
    {
        // Arrange (AC-2) — the chunker yields nothing (blank/scan).
        Document document = GivenExistingDocument();
        _chunker.Chunk(Arg.Any<IReadOnlyList<ExtractedSegment>>()).Returns([]);
        var sut = CreateSut(new ScriptedEmbeddingProvider());

        // Act
        await sut.Handle(new DocumentUploaded(document.Id), CancellationToken.None);

        // Assert
        document.Status.Should().Be(DocumentStatus.Failed);
        document.FailureReason.Should().Be(ProcessingErrors.TextExtractionFailed);
        await _chunkRepository.Received(1).DeleteForDocumentAsync(document, Arg.Any<CancellationToken>());
        await _chunkRepository.DidNotReceive().ReplaceForDocumentAsync(Arg.Any<Document>(), Arg.Any<IReadOnlyList<Chunk>>(), Arg.Any<CancellationToken>());
        _notifier.Received(1).Publish(SessionId, Arg.Is<DocumentStatusUpdate>(u => u.Status == "Failed"));
    }

    [Fact]
    public async Task Should_MarkFailed_When_ExtractionThrows()
    {
        // Arrange (AC-2) — extractor throws (encrypted/corrupt).
        Document document = GivenExistingDocument();
        _extractor.ExtractAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("corrupt"));
        var sut = CreateSut(new ScriptedEmbeddingProvider());

        // Act
        await sut.Handle(new DocumentUploaded(document.Id), CancellationToken.None);

        // Assert
        document.Status.Should().Be(DocumentStatus.Failed);
        document.FailureReason.Should().Be(ProcessingErrors.TextExtractionFailed);
    }

    [Fact]
    public async Task Should_Succeed_When_ProviderFailsThenRecovers()
    {
        // Arrange (AC-3) — provider throws twice, then succeeds (RetryCount = 3).
        Document document = GivenExistingDocument();
        _chunker.Chunk(Arg.Any<IReadOnlyList<ExtractedSegment>>()).Returns([new TextChunk(0, "a", null)]);
        var provider = new ScriptedEmbeddingProvider { FailFirst = 2 };
        var sut = CreateSut(provider);

        // Act
        await sut.Handle(new DocumentUploaded(document.Id), CancellationToken.None);

        // Assert — recovered on the 3rd attempt.
        provider.CallCount.Should().Be(3);
        document.Status.Should().Be(DocumentStatus.Ready);
    }

    [Fact]
    public async Task Should_MarkFailedWithProviderError_And_LeaveNoChunks_When_ProviderKeepsFailing()
    {
        // Arrange (AC-3)
        Document document = GivenExistingDocument();
        _chunker.Chunk(Arg.Any<IReadOnlyList<ExtractedSegment>>()).Returns([new TextChunk(0, "a", null)]);
        var sut = CreateSut(new ScriptedEmbeddingProvider { AlwaysFail = true });

        // Act
        await sut.Handle(new DocumentUploaded(document.Id), CancellationToken.None);

        // Assert — terminal failure with the provider reason; partial index removed; nothing indexed.
        document.Status.Should().Be(DocumentStatus.Failed);
        document.FailureReason.Should().Be(ProcessingErrors.EmbeddingProviderError);
        await _chunkRepository.Received(1).DeleteForDocumentAsync(document, Arg.Any<CancellationToken>());
        await _chunkRepository.DidNotReceive().ReplaceForDocumentAsync(Arg.Any<Document>(), Arg.Any<IReadOnlyList<Chunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_BatchEmbeddingCalls_When_ManyChunks()
    {
        // Arrange (AC-5) — 200 chunks, batch 64 → ceil(200/64) = 4 provider calls.
        Document document = GivenExistingDocument();
        _embedding = new EmbeddingOptions { Dimension = 8, BatchSize = 64, RetryCount = 3 };
        List<TextChunk> many = Enumerable.Range(0, 200).Select(i => new TextChunk(i, $"chunk-{i}", null)).ToList();
        _chunker.Chunk(Arg.Any<IReadOnlyList<ExtractedSegment>>()).Returns(many);
        var provider = new ScriptedEmbeddingProvider();
        var sut = CreateSut(provider);

        // Act
        await sut.Handle(new DocumentUploaded(document.Id), CancellationToken.None);

        // Assert
        provider.CallCount.Should().Be(4);
        document.ChunkCount.Should().Be(200);
    }

    private sealed class ScriptedEmbeddingProvider : IEmbeddingProvider
    {
        public int Dimension { get; set; } = 8;

        public int CallCount { get; private set; }

        public int FailFirst { get; set; }

        public bool AlwaysFail { get; set; }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            CallCount++;
            if (AlwaysFail || CallCount <= FailFirst)
            {
                throw new EmbeddingProviderException("transient");
            }

            IReadOnlyList<float[]> vectors = texts.Select(_ => new float[Dimension]).ToList();

            return Task.FromResult(vectors);
        }
    }
}
