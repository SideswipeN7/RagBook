using RagBook.Shared.Messaging;

namespace RagBook.Modules.Documents.Features.GetQuota;

/// <summary>Reads the current session's quota state for the UI counter (AC-1).</summary>
public sealed record GetQuotaQuery : IQuery<QuotaStateResponse>;
