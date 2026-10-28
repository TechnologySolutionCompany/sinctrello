using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace SincTrello.Infrastructure.Trello;

[SupportedOSPlatform("windows")]
public sealed class WindowsTrelloCredentialStore(string targetName = "SincTrello/Trello/ReadOnly/v1") : ITrelloCredentialStore
{
    private const uint Generic = 1;
    private const int NotFound = 1168;

    public TrelloCredentials? Read()
    {
        if (!CredRead(targetName, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "Não foi possível ler a credencial no Windows.");
        }
        try
        {
            var native = Marshal.PtrToStructure<Credential>(pointer);
            if (native.BlobSize is 0 or > 2560) throw new InvalidDataException("Credencial local inválida. Conecte novamente.");
            var bytes = new byte[native.BlobSize];
            try
            {
                Marshal.Copy(native.Blob, bytes, 0, bytes.Length);
                using var json = JsonDocument.Parse(bytes);
                return new TrelloCredentials(json.RootElement.GetProperty("apiKey").GetString() ?? "", json.RootElement.GetProperty("token").GetString() ?? "");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CredFree(pointer); }
    }

    public void Save(TrelloCredentials credentials)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { apiKey = credentials.ApiKey, token = credentials.Token });
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            if (bytes.Length > 2560) throw new ArgumentException("Credencial maior que o limite do Windows.");
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var native = new Credential { Type = Generic, TargetName = targetName, BlobSize = (uint)bytes.Length, Blob = blob, Persist = 2, UserName = "Trello read" };
            if (!CredWrite(ref native, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível proteger a credencial no Windows.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete()
    {
        if (!CredDelete(targetName, Generic, 0) && Marshal.GetLastWin32Error() != NotFound)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível remover a credencial local.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}
