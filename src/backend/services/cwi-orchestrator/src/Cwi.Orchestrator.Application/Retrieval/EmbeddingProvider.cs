#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace Cwi.Orchestrator.Application.Retrieval;

public interface IEmbeddingProvider
{
    float[] Embed(string text);
}

public sealed class HashEmbeddingProvider : IEmbeddingProvider
{
    private readonly int _dimension;

    public HashEmbeddingProvider(int dimension)
    {
        if (dimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimension), "Embedding dimension must be positive.");
        }

        _dimension = dimension;
    }

    public float[] Embed(string text)
    {
        var buffer = new float[_dimension];
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));

        for (var i = 0; i < bytes.Length; i++)
        {
            var index = i % _dimension;
            buffer[index] += (bytes[i] / 255.0f) - 0.5f;
        }

        var norm = MathF.Sqrt(buffer.Sum(value => value * value));
        if (norm <= 0.0f)
        {
            return buffer;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] /= norm;
        }

        return buffer;
    }
}
#nullable disable
