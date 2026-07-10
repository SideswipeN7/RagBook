using Microsoft.Extensions.Options;
using RagBook.Modules.Documents.Domain;
using RagBook.Modules.Documents.Errors;
using RagBook.Modules.Documents.Quota;
using RagBook.Shared.Messaging;
using RagBook.Shared.Results;

namespace RagBook.Modules.Documents.Features.UploadDocument;

/// <summary>
/// Handles <see cref="UploadDocumentCommand"/> (US-04). Validation order (research D2): reject empty →
/// detect type by content → reject oversize (config per-file limit) → authorize the target folder →
/// store the blob → atomically admit + insert (with per-folder name de-duplication) → publish
/// <see cref="DocumentUploaded"/>. Store-then-record with compensation: if the admit/insert fails the
/// stored blob is deleted, so no orphan file or row remains (FR-012).
/// </summary>
public sealed class UploadDocumentCommandHandler(
    IFileStorage fileStorage,
    IDocumentUploadRepository uploadRepository,
    IFolderReference folderReference,
    IOptions<QuotaOptions> quotaOptions,
    TimeProvider timeProvider,
    IEventPublisher eventPublisher)
{
    /// <summary>Validates, stores, records, and announces the upload — or returns a domain error.</summary>
    public async Task<Result<DocumentResponse>> Handle(UploadDocumentCommand command, CancellationToken cancellationToken)
    {
        if (command.Content.Length == 0)
        {
            return DocumentErrors.EmptyFile;
        }

        Result<SupportedFileType> detection = FileTypeDetector.Detect(command.Content, command.FileName);
        if (detection.IsFailure)
        {
            return Result.Failure<DocumentResponse>(detection.Error);
        }

        QuotaLimits limits = quotaOptions.Value.ToLimits();
        if (command.Content.LongLength > limits.MaxFileSizeBytes)
        {
            return QuotaErrors.FileTooLarge(limits.MaxFileSizeBytes);
        }

        if (command.FolderId is Guid folderId
            && !await folderReference.ExistsInSessionAsync(folderId, cancellationToken))
        {
            return DocumentErrors.TargetFolderNotFound;
        }

        string contentType = detection.Value.ContentType();
        string storagePath;
        using (var content = new MemoryStream(command.Content, writable: false))
        {
            storagePath = await fileStorage.SaveAsync(content, command.FileName, cancellationToken);
        }

        try
        {
            Result<Document> creation = Document.CreateUpload(
                command.Content.LongLength,
                command.FileName,
                contentType,
                command.FolderId,
                storagePath,
                timeProvider.GetUtcNow());
            if (creation.IsFailure)
            {
                await fileStorage.DeleteAsync(storagePath, cancellationToken);

                return Result.Failure<DocumentResponse>(creation.Error);
            }

            Document document = creation.Value;
            Result admission = await uploadRepository.AddUploadedWithinQuotaAsync(document, limits, cancellationToken);
            if (admission.IsFailure)
            {
                await fileStorage.DeleteAsync(storagePath, cancellationToken);

                return Result.Failure<DocumentResponse>(admission.Error);
            }

            await eventPublisher.PublishAsync(new DocumentUploaded(document.Id), cancellationToken);

            return new DocumentResponse(
                document.Id,
                document.FileName!,
                document.ContentType!,
                document.SizeBytes,
                document.Status.ToString(),
                document.FolderId,
                document.UploadedAt!.Value);
        }
        catch
        {
            await fileStorage.DeleteAsync(storagePath, cancellationToken);

            throw;
        }
    }
}
