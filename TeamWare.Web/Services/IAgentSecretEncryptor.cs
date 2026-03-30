namespace TeamWare.Web.Services;

public interface IAgentSecretEncryptor
{
    string? Encrypt(string? plaintext);

    string? Decrypt(string? ciphertext);

    string? MaskForDisplay(string? plaintext);
}
