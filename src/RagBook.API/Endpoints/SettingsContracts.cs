namespace RagBook.API.Endpoints;

/// <summary>Request body for <c>POST /api/settings/api-key</c>. The key is sent in the body only, over HTTPS.</summary>
/// <param name="ApiKey">The full provider key to validate and store.</param>
public sealed record SetApiKeyRequest(string ApiKey);
