namespace RagBook.Modules.Demo;

/// <summary>
/// Fixed identifiers for the demo mode (US-03). <see cref="DemoSessionId"/> is the sentinel owner under which the
/// globally-visible, read-only demo documents are seeded: seeding runs in a scope whose session is initialized to
/// this id, so the existing stamping interceptor writes it (no interceptor change). Demo <b>reads</b> discriminate
/// by <c>Origin == Demo</c> with the per-session filter bypassed — this id only keeps seed writes consistent and
/// gives demo rows a real (non-empty) owner, never another user's.
/// </summary>
public static class DemoConstants
{
    /// <summary>The fixed sentinel session id that owns every seeded demo document.</summary>
    public static readonly Guid DemoSessionId = new("d3300000-0000-4000-8000-000000000001");
}
