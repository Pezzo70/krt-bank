using Domain.ValueObjects;

namespace Domain.Tests.ValueObjects;

public class CpfTests
{
    // Fictitious test CPFs published by Serpro:
    // https://documentacao-sigap-rec.ni.estaleiro.serpro.gov.br/documentacao_api_impedidos/#input
    // Expected contract: Cpf.Create(string) is a static factory.

    [Theory]
    [InlineData("28784142090")]
    [InlineData("08782758000")]
    [InlineData("08940473965")]
    [InlineData("51077358008")]
    [InlineData("62564939074")]
    public void Create_ShouldAcceptValidUnmaskedCpf(string value)
    {
        var result = Cpf.Create(value);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(value, result.Value.Value);
    }

    [Theory]
    [InlineData("287.841.420-90", "28784142090")]
    [InlineData("087.827.580-00", "08782758000")]
    [InlineData("510.773.580-08", "51077358008")]
    [InlineData(" 287.841.420-90 ", "28784142090")]
    [InlineData("\t08782758000\r\n", "08782758000")]
    public void Create_ShouldNormalizeValidCpf(string input, string expected)
    {
        var result = Cpf.Create(input);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Create_ShouldRejectNullOrWhitespace(string? value)
    {
        AssertInvalidCpf(value);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("287841420")]
    [InlineData("2878414209")]
    [InlineData("287841420900")]
    [InlineData("287.841.420-9")]
    public void Create_ShouldRejectInvalidLength(string value)
    {
        AssertInvalidCpf(value);
    }

    [Theory]
    [InlineData("abcdefghijk")]
    [InlineData("2878414209A")]
    [InlineData("A28784142090")]
    [InlineData("28784142090A")]
    [InlineData("287.841.420-90A")]
    public void Create_ShouldRejectLetters(string value)
    {
        AssertInvalidCpf(value);
    }

    [Theory]
    [InlineData("287.841.420/90")]
    [InlineData("287-841-420-90")]
    [InlineData("28784142090.")]
    [InlineData("287.841420-90")]
    [InlineData("287 841 420 90")]
    public void Create_ShouldRejectInvalidMask(string value)
    {
        AssertInvalidCpf(value);
    }

    [Theory]
    [InlineData("00000000000")]
    [InlineData("11111111111")]
    [InlineData("22222222222")]
    [InlineData("33333333333")]
    [InlineData("44444444444")]
    [InlineData("55555555555")]
    [InlineData("66666666666")]
    [InlineData("77777777777")]
    [InlineData("88888888888")]
    [InlineData("99999999999")]
    [InlineData("111.111.111-11")]
    public void Create_ShouldRejectRepeatedDigits(string value)
    {
        AssertInvalidCpf(value);
    }

    [Theory]
    // The second check digit was recalculated for the incorrect first check digit.
    // This tests rejection of the first check digit independently of the second.
    [InlineData("28784142081")]
    [InlineData("62564939066")]
    public void Create_ShouldRejectInvalidFirstCheckDigit(string value)
    {
        AssertInvalidCpf(value);
    }

    [Theory]
    [InlineData("28784142091")]
    [InlineData("08782758001")]
    [InlineData("62564939075")]
    public void Create_ShouldRejectInvalidSecondCheckDigit(string value)
    {
        AssertInvalidCpf(value);
    }

    [Fact]
    public void Equality_ShouldMatchNormalizedValues()
    {
        var first = CreateValidCpf("28784142090");
        var second = CreateValidCpf("287.841.420-90");

        Assert.Equal(first, second);
        Assert.True(first == second);
    }

    [Fact]
    public void Equality_ShouldDistinguishDifferentValues()
    {
        var first = CreateValidCpf("28784142090");
        var second = CreateValidCpf("62564939074");

        Assert.NotEqual(first, second);
        Assert.False(first == second);
    }

    [Fact]
    public void HashSet_ShouldDeduplicateEquivalentCpfs()
    {
        var first = CreateValidCpf("28784142090");
        var second = CreateValidCpf("287.841.420-90");

        var cpfs = new HashSet<Cpf> { first, second };

        Assert.Single(cpfs);
    }

    private static Cpf CreateValidCpf(string value)
    {
        var result = Cpf.Create(value);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        return result.Value;
    }

    private static void AssertInvalidCpf(string? value)
    {
        // Null is intentional to verify runtime input handling.
        var result = Cpf.Create(value!);

        Assert.True(result.IsFailed);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error =>
            Assert.False(string.IsNullOrWhiteSpace(error.Message)));
    }
}
