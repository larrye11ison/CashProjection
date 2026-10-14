using System.Runtime.InteropServices;
using System.Text;

namespace CashProjection.Services.Secrets
{
    // Stores secrets in the Windows Credential Manager (generic credentials,
    // local-machine persistence). This is the mechanism used for anything
    // that must survive and be reusable across both Debug and Release builds
    // without ever touching source-controlled files: Production client
    // credentials, and the exchanged Plaid access_token in both Sandbox and
    // Production (so the one real Link call's result is never lost).
    public sealed class WindowsCredentialManagerSecretStore : ISecretStore
    {
        private const string TargetPrefix = "CashProjection:";
        private const uint CredTypeGeneric = 1;
        private const uint CredPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
        private static extern void CredFree(IntPtr cred);

        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, int flags);

        public string? Get(string key)
        {
            var target = TargetPrefix + key;
            if (!CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
                return null;

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                    return null;

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        public void Set(string key, string value)
        {
            var target = TargetPrefix + key;
            var targetNamePtr = Marshal.StringToHGlobalUni(target);
            var blobBytes = Encoding.Unicode.GetBytes(value);
            var blobPtr = Marshal.AllocHGlobal(blobBytes.Length);

            try
            {
                Marshal.Copy(blobBytes, 0, blobPtr, blobBytes.Length);

                var credential = new CREDENTIAL
                {
                    Flags = 0,
                    Type = CredTypeGeneric,
                    TargetName = targetNamePtr,
                    Comment = IntPtr.Zero,
                    LastWritten = 0,
                    CredentialBlobSize = (uint)blobBytes.Length,
                    CredentialBlob = blobPtr,
                    Persist = CredPersistLocalMachine,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = IntPtr.Zero,
                    UserName = IntPtr.Zero,
                };

                if (!CredWrite(ref credential, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Failed to write credential '{key}' (Win32 error {error}).");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(targetNamePtr);
                Marshal.FreeHGlobal(blobPtr);
            }
        }

        public void Remove(string key)
        {
            var target = TargetPrefix + key;
            if (!CredDelete(target, CredTypeGeneric, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorNotFound)
                    throw new InvalidOperationException($"Failed to delete credential '{key}' (Win32 error {error}).");
            }
        }
    }
}
