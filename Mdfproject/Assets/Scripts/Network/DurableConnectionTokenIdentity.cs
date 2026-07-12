using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Raw Photon connection tokens must remain server-local. Durable reconnect identity is
/// replicated and migrated only as a one-way SHA-256 fingerprint.
/// </summary>
public static class DurableConnectionTokenIdentity
{
    public static string BuildHash(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        var builder = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2"));
        }
        return builder.ToString();
    }
}
