using Xunit; using SourceFixture; namespace SourceFixtureTests;
public sealed class RequiredFacts {
    [Fact] public void AlphaDesired() { Assert.Equal(2,Alpha.Value()); }
    [Fact] public void BetaDesired() { Assert.Equal(2,Beta.Value()); }
    [Fact] public void GammaBounded() { Assert.InRange(Gamma.Value(),1,2); }
}
