using RagBook.Shared.Messaging;

namespace RagBook.Modules.Documents.Features.UploadDocument;

/// <summary>
/// Published in-process after a document is durably stored and recorded (US-04). It is the seam
/// background processing (US-06) subscribes to for chunking and embeddings; US-04 ships no subscriber.
/// </summary>
/// <param name="DocumentId">The newly uploaded document.</param>
public sealed record DocumentUploaded(Guid DocumentId) : IEvent;
