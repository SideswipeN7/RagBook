namespace RagBook.Modules.Session.Features;

/// <summary>Read model for a session-owned resource returned to the client.</summary>
/// <param name="Id">Resource identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="CreatedAt">UTC creation instant.</param>
public sealed record ResourceResponse(Guid Id, string Name, DateTimeOffset CreatedAt);
