namespace TeamWare.Web.Services;

/// <summary>
/// Provides encryption, decryption, and masking for agent secret values
/// (access tokens, auth headers, environment variables).
/// Uses ASP.NET Core Data Protection with purpose string "AgentSecrets.v1".
/// </summary>
public interface IAgentSecretEncryptor
{
    /// <summary>Encrypts a plaintext secret. Returns null if input is null or empty.</summary>
    string? Encrypt(string? plaintext);

    /// <summary>Decrypts a previously encrypted secret. Returns null if input is null or empty.</summary>
    string? Decrypt(string? ciphertext);

    /// <summary>
    /// Returns a masked representation for display (e.g., "ghp_****xyz").
    /// Shows first 4 and last 3 characters; tokens ≤ 8 characters return "****".
    /// Returns null if input is null or empty.
    /// </summary>
    string? MaskForDisplay(string? plaintext);
}
