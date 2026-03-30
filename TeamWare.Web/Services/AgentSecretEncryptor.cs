using Microsoft.AspNetCore.DataProtection;

namespace TeamWare.Web.Services;

public class AgentSecretEncryptor : IAgentSecretEncryptor
{
    private readonly IDataProtector _protector;

    public AgentSecretEncryptor(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("AgentSecrets.v1");
    }

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return null;

        return _protector.Protect(plaintext);
    }

    public string? Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return null;

        return _protector.Unprotect(ciphertext);
    }

    public string? MaskForDisplay(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return null;

        if (plaintext.Length <= 8)
            return "****";

        return $"{plaintext[..4]}****{plaintext[^3..]}";
    }
}
