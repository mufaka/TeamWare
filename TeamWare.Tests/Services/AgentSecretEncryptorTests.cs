using Microsoft.AspNetCore.DataProtection;
using TeamWare.Web.Services;

namespace TeamWare.Tests.Services;

public class AgentSecretEncryptorTests
{
    private readonly AgentSecretEncryptor _encryptor;

    public AgentSecretEncryptorTests()
    {
        var provider = DataProtectionProvider.Create("TeamWare.Tests");
        _encryptor = new AgentSecretEncryptor(provider);
    }

    [Fact]
    public void Encrypt_NullInput_ReturnsNull()
    {
        Assert.Null(_encryptor.Encrypt(null));
    }

    [Fact]
    public void Encrypt_EmptyInput_ReturnsNull()
    {
        Assert.Null(_encryptor.Encrypt(string.Empty));
    }

    [Fact]
    public void Decrypt_NullInput_ReturnsNull()
    {
        Assert.Null(_encryptor.Decrypt(null));
    }

    [Fact]
    public void Decrypt_EmptyInput_ReturnsNull()
    {
        Assert.Null(_encryptor.Decrypt(string.Empty));
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var original = "ghp_abc123secrettoken456";

        var encrypted = _encryptor.Encrypt(original);
        Assert.NotNull(encrypted);
        Assert.NotEqual(original, encrypted);

        var decrypted = _encryptor.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_LongToken()
    {
        var original = new string('x', 500);

        var encrypted = _encryptor.Encrypt(original);
        var decrypted = _encryptor.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_UnicodeContent()
    {
        var original = "token_with_émojis_🎉_and_ünïcödë";

        var encrypted = _encryptor.Encrypt(original);
        var decrypted = _encryptor.Decrypt(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void MaskForDisplay_NullInput_ReturnsNull()
    {
        Assert.Null(_encryptor.MaskForDisplay(null));
    }

    [Fact]
    public void MaskForDisplay_EmptyInput_ReturnsNull()
    {
        Assert.Null(_encryptor.MaskForDisplay(string.Empty));
    }

    [Fact]
    public void MaskForDisplay_ShortToken_ReturnsMaskOnly()
    {
        Assert.Equal("****", _encryptor.MaskForDisplay("abc"));
    }

    [Fact]
    public void MaskForDisplay_ExactlyEightChars_ReturnsMaskOnly()
    {
        Assert.Equal("****", _encryptor.MaskForDisplay("12345678"));
    }

    [Fact]
    public void MaskForDisplay_NineChars_ShowsFirstFourLastThree()
    {
        // "123456789" → "1234****789"
        var result = _encryptor.MaskForDisplay("123456789");
        Assert.Equal("1234****789", result);
    }

    [Fact]
    public void MaskForDisplay_TypicalGitHubToken()
    {
        // "ghp_abc123secrettoken456xyz" → "ghp_****xyz"
        var result = _encryptor.MaskForDisplay("ghp_abc123secrettoken456xyz");
        Assert.Equal("ghp_****xyz", result);
    }

    [Fact]
    public void MaskForDisplay_SingleChar_ReturnsMaskOnly()
    {
        Assert.Equal("****", _encryptor.MaskForDisplay("x"));
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachCall()
    {
        var original = "same-secret";

        var encrypted1 = _encryptor.Encrypt(original);
        var encrypted2 = _encryptor.Encrypt(original);

        // Data Protection uses random IVs, so ciphertexts should differ
        Assert.NotEqual(encrypted1, encrypted2);

        // Both should decrypt to the same value
        Assert.Equal(original, _encryptor.Decrypt(encrypted1));
        Assert.Equal(original, _encryptor.Decrypt(encrypted2));
    }
}
