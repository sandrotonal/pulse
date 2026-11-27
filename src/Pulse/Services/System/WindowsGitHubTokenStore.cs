using Pulse.Core.Interfaces;
using Pulse.Core.Services;
using Windows.Security.Credentials;

namespace Pulse.Services.System;

/// <summary>
/// Stores optional GitHub Personal Access Tokens in Windows Credential Manager (<see cref="PasswordVault"/>)
/// so secrets never touch <c>%LOCALAPPDATA%\Pulse\settings.json</c>.
/// </summary>
public sealed class WindowsGitHubTokenStore : IGitHubTokenStore
{
    private const string ResourceName = "PulseGitHubWorkspaceToken";
    private const string DefaultUserName = "github-pat";

    public string? GetToken()
    {
        try
        {
            var vault = new PasswordVault();
            var credentials = vault.FindAllByResource(ResourceName);
            var entry = credentials.FirstOrDefault();
            if (entry is null)
            {
                return null;
            }

            entry.RetrievePassword();
            return string.IsNullOrWhiteSpace(entry.Password) ? null : entry.Password;
        }
        catch
        {
            // Element not found (COMException 0x80070490) or Credential Manager unavailable.
            return null;
        }
    }

    public bool SaveToken(string? token)
    {
        var trimmed = token?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            ClearToken();
            return true;
        }

        try
        {
            ClearToken();
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(ResourceName, DefaultUserName, trimmed));
            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("WindowsGitHubTokenStore.SaveToken", ex);
            return false;
        }
    }

    public void ClearToken()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var existing in vault.FindAllByResource(ResourceName).ToList())
            {
                vault.Remove(existing);
            }
        }
        catch
        {
        }
    }

    public bool HasToken() => !string.IsNullOrWhiteSpace(GetToken());
}
