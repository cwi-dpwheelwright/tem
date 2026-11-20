namespace Cwi.Orchestrator.Application.Retrieval;

public sealed class RetrievalResult
{
    public string DocId { get; }
    public string ChunkId { get; }
    public string Content { get; }
    public float Score { get; }

    public RetrievalResult(string docId, string chunkId, string content, float score)
    {
        DocId = docId;
        ChunkId = chunkId;
        Content = content;
        Score = score;
    }
}
