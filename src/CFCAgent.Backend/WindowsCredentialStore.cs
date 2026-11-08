using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace CFCAgent.Backend;

// Stores secrets in Windows Credential Manager (the "Generic" credential type), so they are
// encrypted by Windows for the signed-in user and never sit in a file or an environment variable.
// Entries are kept on this machine only and show up in Windows' own Credential Manager screen
// under the target name. Calls the Win32 CredRead/CredWrite/CredDelete functions directly, so it
// needs no extra package. Any process running as the same Windows user can read what is stored.
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    // Windows limits a generic credential's secret to 5 * 512 bytes.
    private const int MaxSecretBytes = 2560;

    public string? Get(string target)
    {
        EnsureWindows();

        if (!CredRead(target, CredTypeGeneric, 0, out IntPtr buffer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(buffer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(buffer);
        }
    }

    public void Set(string target, string userName, string secret)
    {
        EnsureWindows();

        byte[] secretBytes = Encoding.Unicode.GetBytes(secret);
        if (secretBytes.Length > MaxSecretBytes)
        {
            throw new ArgumentException($"The secret is too long for Windows Credential Manager ({secretBytes.Length} bytes; the limit is {MaxSecretBytes}).", nameof(secret));
        }

        IntPtr secretBuffer = Marshal.AllocHGlobal(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, secretBuffer, secretBytes.Length);

            NativeCredential credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = secretBuffer,
                Persist = CredPersistLocalMachine,
                UserName = userName,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.Copy(new byte[secretBytes.Length], 0, secretBuffer, secretBytes.Length);
            Marshal.FreeHGlobal(secretBuffer);
            Array.Clear(secretBytes);
        }
    }

    public bool Delete(string target)
    {
        EnsureWindows();

        if (CredDelete(target, CredTypeGeneric, 0))
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new Win32Exception(error);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager is only available on Windows.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
