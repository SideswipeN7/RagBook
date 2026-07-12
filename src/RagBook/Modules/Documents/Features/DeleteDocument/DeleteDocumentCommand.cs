using RagBook.Shared.Messaging;

namespace RagBook.Modules.Documents.Features.DeleteDocument;

/// <summary>Permanently deletes a session-owned document and its whole index (US-08).</summary>
/// <param name="Id">The document to delete.</param>
public sealed record DeleteDocumentCommand(Guid Id) : ICommand;
