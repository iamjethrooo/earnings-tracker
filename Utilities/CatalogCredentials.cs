using System.ComponentModel;
using System.Runtime.InteropServices;

namespace nerve_earnings_tracker.Utilities;

internal static class CatalogCredentials
{
    private const string Target = "NerveEarningsTracker/IsThereAnyDeal";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Read(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Delete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);

    internal static string? Get()
    {
        if (!Read(Target, 1, 0, out var ptr))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(ptr);
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(ptr); }
    }

    internal static void Save(string key)
    {
        if (key.Length > 1000) throw new ArgumentException("API key is too long.");
        var ptr = Marshal.StringToCoTaskMemUni(key);
        try
        {
            var credential = new Credential { Type = 1, TargetName = Target, UserName = "IsThereAnyDeal",
                Comment = "Optional game price refresh", CredentialBlob = ptr,
                CredentialBlobSize = (uint)key.Length * 2, Persist = 2, TargetAlias = "" };
            if (!Write(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(ptr); }
    }

    internal static void Remove()
    {
        if (!Delete(Target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
