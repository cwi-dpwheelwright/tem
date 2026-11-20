using Xunit;

namespace Cwi.Router.UnitTests;

[CollectionDefinition("Env", DisableParallelization = true)]
public sealed class EnvCollection : ICollectionFixture<EnvFixture>
{
}

public sealed class EnvFixture
{
}
