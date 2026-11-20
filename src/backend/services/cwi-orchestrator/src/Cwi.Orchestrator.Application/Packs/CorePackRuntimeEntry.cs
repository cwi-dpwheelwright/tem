namespace Cwi.Orchestrator.Application.Packs;

public sealed class CorePackRuntimeEntry
{
    public PackDocument Document { get; }
    public BaselinePackGuidance Guidance { get; }
    public string PackRef { get; }
    public string PackRootPath { get; }
    public string AnchorPath { get; }
    public string ContentPath { get; }
    public DateTimeOffset LoadedAtUtc { get; }

    public CorePackRuntimeEntry(
        PackDocument document,
        BaselinePackGuidance guidance,
        string packRef,
        string packRootPath,
        string anchorPath,
        string contentPath,
        DateTimeOffset loadedAtUtc)
    {
        Document = document;
        Guidance = guidance;
        PackRef = packRef;
        PackRootPath = packRootPath;
        AnchorPath = anchorPath;
        ContentPath = contentPath;
        LoadedAtUtc = loadedAtUtc;
    }
}
