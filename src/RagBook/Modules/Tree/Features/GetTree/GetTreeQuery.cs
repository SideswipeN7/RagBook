using RagBook.Shared.Messaging;

namespace RagBook.Modules.Tree.Features.GetTree;

/// <summary>Reads the current session's folders + documents for the tree view in one response (US-07).</summary>
public sealed record GetTreeQuery : IQuery<TreeResponse>;
