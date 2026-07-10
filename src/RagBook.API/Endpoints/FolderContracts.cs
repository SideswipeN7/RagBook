namespace RagBook.API.Endpoints;

/// <summary>Request body for <c>POST /api/folders</c>.</summary>
/// <param name="Name">The folder name.</param>
/// <param name="ParentId">Parent folder id, or <c>null</c> to create a root folder.</param>
public sealed record CreateFolderRequest(string Name, Guid? ParentId);

/// <summary>Response body for a created folder.</summary>
/// <param name="Id">The new folder's identifier.</param>
public sealed record CreateFolderResponse(Guid Id);

/// <summary>Request body for <c>PUT /api/folders/{id}/name</c>.</summary>
/// <param name="Name">The new folder name.</param>
public sealed record RenameFolderRequest(string Name);
