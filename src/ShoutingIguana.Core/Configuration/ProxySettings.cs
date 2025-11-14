using System.Runtime.Versioning;

namespace ShoutingIguana.Core.Configuration;

/// <summary>
/// Proxy type enumeration.
/// </summary>
public enum ProxyType
{
    None,
    Http,
    Https,
    Socks5
}

/// <summary>
/// Proxy configuration settings.
/// </summary>
[SupportedOSPlatform("windows")]
public class ProxySettings
{
    public bool Enabled { get; set; }
    public ProxyType Type { get; set; } = ProxyType.Http;
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public bool RequiresAuthentication { get; set; }
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// Encrypted password (using DPAPI).
    /// </summary>
    public string EncryptedPassword { get; set; } = string.Empty;
    
    /// <summary>
    /// Bypass list - patterns to exclude from proxy (e.g., localhost, *.internal.com).
    /// </summary>
    public List<string> BypassList { get; set; } = [];

    /// <summary>
    /// Gets the proxy URL formatted for Playwright.
    /// </summary>
    public string GetProxyUrl()
    {
        if (!Enabled || string.IsNullOrWhiteSpace(Server))
            return string.Empty;

        var scheme = Type switch
        {
            ProxyType.Http => "http",
            ProxyType.Https => "https",
            ProxyType.Socks5 => "socks5",
            _ => "http"
        };

        return $"{scheme}://{Server}:{Port}";
    }

    /// <summary>
    /// Sets the password and encrypts it using DPAPI.
    /// </summary>
    public void SetPassword(string plainPassword)
    {
        if (string.IsNullOrEmpty(plainPassword))
        {
            EncryptedPassword = string.Empty;
            return;
        }

        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainPassword);
        var encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(
            plainBytes,
            null,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        EncryptedPassword = Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// Gets the decrypted password using DPAPI.
    /// </summary>
    public string GetPassword()
    {
        if (string.IsNullOrEmpty(EncryptedPassword))
            return string.Empty;

        try
        {
            var encryptedBytes = Convert.FromBase64String(EncryptedPassword);
            var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                encryptedBytes,
                null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Clones the proxy settings.
    /// </summary>
    public ProxySettings Clone()
    {
        return new ProxySettings
        {
            Enabled = Enabled,
            Type = Type,
            Server = Server,
            Port = Port,
            RequiresAuthentication = RequiresAuthentication,
            Username = Username,
            EncryptedPassword = EncryptedPassword,
            BypassList = new List<string>(BypassList)
        };
    }
}

