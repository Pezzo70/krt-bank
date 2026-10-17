using Domain.ValueObjects;

namespace Domain.Tests.ValueObjects;

public class CpfTests
{
    // CPFs fictícios de homologação publicados pelo Serpro:
    // https://documentacao-sigap-rec.ni.estaleiro.serpro.gov.br/documentacao_api_impedidos/#entrada
    // Contrato esperado: Cpf.Create(string) é uma fábrica estática.

    [Theory]
    [InlineData("28784142090")]
    [InlineData("08782758000")]
    [InlineData("08940473965")]
    [InlineData("51077358008")]
    [InlineData("62564939074")]
    public void Create_DeveCriarCpfValidoSemMascara(string valor)
    {
        var resultado = Cpf.Create(valor);

        Assert.True(resultado.IsSuccess);
        Assert.NotNull(resultado.Value);
        Assert.Equal(valor, resultado.Value.Value);
    }

    [Theory]
    [InlineData("287.841.420-90", "28784142090")]
    [InlineData("087.827.580-00", "08782758000")]
    [InlineData("510.773.580-08", "51077358008")]
    [InlineData(" 287.841.420-90 ", "28784142090")]
    [InlineData("\t08782758000\r\n", "08782758000")]
    public void Create_DeveNormalizarCpfValido(string entrada, string esperado)
    {
        var resultado = Cpf.Create(entrada);

        Assert.True(resultado.IsSuccess);
        Assert.NotNull(resultado.Value);
        Assert.Equal(esperado, resultado.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Create_DeveRejeitarValorNuloOuEmBranco(string? valor)
    {
        AssertCpfInvalido(valor);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("287841420")]
    [InlineData("2878414209")]
    [InlineData("287841420900")]
    [InlineData("287.841.420-9")]
    public void Create_DeveRejeitarQuantidadeDeDigitosInvalida(string valor)
    {
        AssertCpfInvalido(valor);
    }

    [Theory]
    [InlineData("abcdefghijk")]
    [InlineData("2878414209A")]
    [InlineData("A28784142090")]
    [InlineData("28784142090A")]
    [InlineData("287.841.420-90A")]
    public void Create_DeveRejeitarLetras(string valor)
    {
        AssertCpfInvalido(valor);
    }

    [Theory]
    [InlineData("287.841.420/90")]
    [InlineData("287-841-420-90")]
    [InlineData("28784142090.")]
    [InlineData("287.841420-90")]
    [InlineData("287 841 420 90")]
    public void Create_DeveRejeitarMascaraInvalida(string valor)
    {
        AssertCpfInvalido(valor);
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
    public void Create_DeveRejeitarDigitosTodosIguais(string valor)
    {
        AssertCpfInvalido(valor);
    }

    [Theory]
    // O segundo dígito foi recalculado para o primeiro dígito incorreto.
    // Isso verifica a rejeição do primeiro verificador independentemente do segundo.
    [InlineData("28784142081")]
    [InlineData("62564939066")]
    public void Create_DeveRejeitarPrimeiroDigitoVerificadorInvalido(string valor)
    {
        AssertCpfInvalido(valor);
    }

    [Theory]
    [InlineData("28784142091")]
    [InlineData("08782758001")]
    [InlineData("62564939075")]
    public void Create_DeveRejeitarSegundoDigitoVerificadorInvalido(string valor)
    {
        AssertCpfInvalido(valor);
    }

    [Fact]
    public void DeveConsiderarIguaisCpfsComMesmoNumeroNormalizado()
    {
        var primeiro = CriarCpfValido("28784142090");
        var segundo = CriarCpfValido("287.841.420-90");

        Assert.Equal(primeiro, segundo);
        Assert.True(primeiro == segundo);
    }

    [Fact]
    public void DeveConsiderarDiferentesCpfsComNumerosDiferentes()
    {
        var primeiro = CriarCpfValido("28784142090");
        var segundo = CriarCpfValido("62564939074");

        Assert.NotEqual(primeiro, segundo);
        Assert.False(primeiro == segundo);
    }

    [Fact]
    public void DeveReconhecerMesmoCpfEmHashSet()
    {
        var primeiro = CriarCpfValido("28784142090");
        var segundo = CriarCpfValido("287.841.420-90");

        var cpfs = new HashSet<Cpf> { primeiro, segundo };

        Assert.Single(cpfs);
    }

    private static Cpf CriarCpfValido(string valor)
    {
        var resultado = Cpf.Create(valor);

        Assert.True(resultado.IsSuccess);
        Assert.NotNull(resultado.Value);

        return resultado.Value;
    }

    private static void AssertCpfInvalido(string? valor)
    {
        // null é deliberado para verificar a entrada em tempo de execução.
        var resultado = Cpf.Create(valor!);

        Assert.True(resultado.IsFailed);
        Assert.NotEmpty(resultado.Errors);
        Assert.All(resultado.Errors, erro =>
            Assert.False(string.IsNullOrWhiteSpace(erro.Message)));
    }
}
