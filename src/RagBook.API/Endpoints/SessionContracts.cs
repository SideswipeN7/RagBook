namespace RagBook.API.Endpoints;

/// <summary>Empty-session application state returned by <c>GET /api/session</c>.</summary>
/// <param name="IsNew">True when this request minted the session.</param>
/// <param name="ResourceCount">Number of resources owned by the current session.</param>
public sealed record SessionStateResponse(bool IsNew, int ResourceCount);

/// <summary>Request body for <c>POST /api/resources</c>.</summary>
/// <param name="Name">The resource name.</param>
public sealed record CreateResourceRequest(string Name);

/// <summary>Response body for a created resource.</summary>
/// <param name="Id">The new resource's identifier.</param>
public sealed record CreateResourceResponse(Guid Id);
