using System.IO;
using System.Text.Json;

namespace CashProjection.Services.Secrets
{
    // Reads the gitignored plaid.sandbox.secrets.json file at the repo root.
    // This file holds ONLY Sandbox client_id/secret — never a real access
    // token and never Production credentials. It is manually maintained by
    // the developer, so Set/Remove are not supported.
    public sealed class DevFileSecretStore : ISecretStore
    {
        private const string FileName = "plaid.sandbox.secrets.json";

        private sealed class SandboxSecretsFile
        {
            public string? ClientId { get; set; }
            public string? SandboxSecret { get; set; }
        }

        private readonly Lazy<SandboxSecretsFile?> _file = new(Load);

        public string? Get(string key)
        {
            var file = _file.Value;
            if (file is null)
                return null;

            return key switch
            {
                "Plaid:ClientId" => file.ClientId,
                "Plaid:SandboxSecret" => file.SandboxSecret,
                _ => null,
            };
        }

        public void Set(string key, string value) =>
            throw new NotSupportedException(
                $"{FileName} is manually maintained and cannot be written to by the app.");

        public void Remove(string key) =>
            throw new NotSupportedException(
                $"{FileName} is manually maintained and cannot be written to by the app.");

        private static SandboxSecretsFile? Load()
        {
            var path = FindRepoRootFile();
            if (path is null || !File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SandboxSecretsFile>(json);
        }

        // Walks up from the running assembly's directory looking for a
        // directory containing a .sln file (the repo root), since the dev
        // secrets file lives there, not in bin/Debug/....
        private static string? FindRepoRootFile()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (dir.GetFiles("*.sln").Length > 0)
                {
                    var candidate = Path.Combine(dir.FullName, FileName);
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
