using System.Text.Json;
using SincTrello.Core;

namespace SincTrello.Infrastructure.Storage;

public interface IFileSystemService
{
    void ValidateRoot(string root);
    void ValidatePath(string root, string path);
    IEnumerable<string> EnumerateEntries(string directory);
    bool IsDirectory(string path);
    bool Exists(string path);
    bool IsReparsePoint(string path);
    string ResolveCardPath(string root, string relative);
    T ReadMetadata<T>(string root, string path);
    void WriteMetadata<T>(string root, string path, T value, bool replace);
    IDisposable AcquireLock(string root);
    void CreateCardFolder(string root, string relative, CardFolderMetadata metadata);
    void MoveCardFolder(string root, string source, string destination);
}

/// <summary>All mutation targets are confined to the configured root; user files are never deleted or replaced.</summary>
public sealed class SafeLocalFileSystem : IFileSystemService
{
    public const string RootMetadataName = ".stroot.json";
    public const string CardMetadataName = ".stmeta.json";
    public const string StagePrefix = ".st-stage-";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public void ValidateRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\?\", StringComparison.Ordinal) || root.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new SyncValidationException("Escolha uma pasta principal com caminho completo.");
        if (!Directory.Exists(root)) throw new SyncValidationException("A pasta principal está indisponível. Conecte o disco ou selecione uma pasta existente.");
        var current = new DirectoryInfo(Path.GetFullPath(root));
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new SyncValidationException("A pasta principal não pode atravessar atalhos de diretório, junctions ou links simbólicos.");
            current = current.Parent;
        }
    }
    public void ValidatePath(string root, string path)
    {
        ValidateRoot(root);
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.EndsInDirectorySeparator(canonical) ? canonical : canonical + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new SyncValidationException("Um caminho saiu da pasta principal. Refaça a verificação.");
        var current = full;
        while (!string.Equals(current, canonical, StringComparison.OrdinalIgnoreCase))
        {
            if (IsReparsePoint(current)) throw new SyncValidationException($"Link simbólico ou junction bloqueia a operação: {Path.GetRelativePath(root, current)}");
            current = Path.GetDirectoryName(current) ?? throw new SyncValidationException("Caminho inválido.");
        }
    }
    public IEnumerable<string> EnumerateEntries(string directory) => Directory.EnumerateFileSystemEntries(directory);
    public bool IsDirectory(string path) => Directory.Exists(path);
    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    public bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    public T ReadMetadata<T>(string root, string path)
    {
        ValidatePath(root, path);
        if (new FileInfo(path).Length > 65536) throw new SyncValidationException("Metadado local excede o tamanho permitido. Revise a pasta antes de sincronizar.");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new SyncValidationException("Metadado local vazio ou inválido.");
        }
        catch (JsonException) { throw new SyncValidationException($"Metadado local inválido: {Path.GetRelativePath(root, path)}"); }
    }
    public void WriteMetadata<T>(string root, string path, T value, bool replace)
    {
        ValidatePath(root, path);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        ValidatePath(root, temporary);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
        ValidatePath(root, path);
        File.Move(temporary, path, replace);
        if (OperatingSystem.IsWindows()) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }
    public IDisposable AcquireLock(string root)
    {
        var path = Path.Combine(root, ".stsync.lock");
        ValidatePath(root, path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new SyncValidationException("A pasta está sendo usada por outra sincronização, ou não permite gravação. Aguarde e verifique as permissões."); }
    }
    public void CreateCardFolder(string root, string relative, CardFolderMetadata metadata)
    {
        var destination = ResolveCardPath(root, relative);
        EnsureListFolder(root, destination);
        if (Exists(destination)) throw new SyncValidationException($"Destino ocupado: {relative}");
        var stage = Path.Combine(root, StagePrefix + Guid.NewGuid().ToString("N"));
        ValidatePath(root, stage);
        if (Exists(stage)) throw new SyncValidationException("Não foi possível reservar a pasta temporária.");
        Directory.CreateDirectory(stage);
        WriteMetadata(root, Path.Combine(stage, CardMetadataName), metadata, false);
        ValidatePath(root, destination);
        Directory.Move(stage, destination); // Atomic publication on the same volume; fails if destination exists.
    }
    public void MoveCardFolder(string root, string source, string destination)
    {
        var from = ResolveCardPath(root, source);
        var to = ResolveCardPath(root, destination);
        EnsureListFolder(root, to);
        ValidatePath(root, from); ValidatePath(root, to);
        var pending = new Stack<string>(); pending.Push(from);
        while (pending.TryPop(out var directory))
            foreach (var entry in EnumerateEntries(directory))
            { ValidatePath(root, entry); if (IsDirectory(entry)) pending.Push(entry); }
        if (Exists(to)) throw new SyncValidationException($"Destino ocupado: {destination}");
        Directory.Move(from, to); // Moves the whole folder, including SQL, Excel and BGR files.
    }
    public string ResolveCardPath(string root, string relative)
    {
        var parts = relative.Split('\\');
        if (parts.Length != 2 || !parts.All(WindowsNames.IsValid)) throw new SyncValidationException("Caminho de demanda inválido.");
        var path = Path.Combine(root, parts[0], parts[1]);
        if (path.Length > 240) throw new SyncValidationException($"Caminho muito longo: {relative}. Use uma raiz mais curta ou reduza o nome no Trello.");
        ValidatePath(root, path);
        return path;
    }
    private void EnsureListFolder(string root, string destination)
    {
        var parent = Path.GetDirectoryName(destination)!;
        ValidatePath(root, parent);
        if (File.Exists(parent)) throw new SyncValidationException("Um arquivo ocupa o nome de uma lista. Revise a pasta principal.");
        Directory.CreateDirectory(parent);
        ValidatePath(root, parent);
    }
}
