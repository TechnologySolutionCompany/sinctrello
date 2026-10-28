using SincTrello.Core;

namespace SincTrello.Infrastructure.Storage;

public sealed class LocalFolderSyncService(IFileSystemService files, LocalSyncRepository repository, ISyncEngine engine)
{
    public Task<PreparedFolderSync> PrepareAsync(string rootPath, BoardSnapshot board, string memberId, CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(rootPath, board, memberId, null, cancellationToken), cancellationToken);
    public Task<SyncExecutionResult> ApplyAsync(PreparedFolderSync approved, BoardSnapshot latestBoard, string memberId,
        IProgress<SyncExecutionProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Apply(approved, latestBoard, memberId, progress, cancellationToken), CancellationToken.None);
    public Task<IReadOnlyList<SyncHistoryEntry>> ReadHistoryAsync() => Task.Run(repository.ReadHistory);

    private PreparedFolderSync Prepare(string rootPath, BoardSnapshot board, string memberId, SyncRootMarker? prospective, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
            throw new SyncValidationException("Defina e salve uma pasta principal existente em Configurações.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var desired = DesiredStateBuilder.Build(board, new AssignedMemberCardFilter(memberId));
        var marker = prospective ?? new SyncRootMarker(1, Guid.NewGuid().ToString("N"), board.Id);
        var actual = new List<ActualCardState>(); var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<string>(); var known = Array.Empty<string>(); var available = false;
        try
        {
            files.ValidateRoot(root); available = true;
            var markerPath = Path.Combine(root, SafeLocalFileSystem.RootMetadataName);
            var hasMarker = files.Exists(markerPath);
            if (hasMarker)
            {
                marker = files.ReadMetadata<SyncRootMarker>(root, markerPath);
                ValidateMarker(marker, board.Id);
            }
            else if (repository.IsRegisteredPath(root)) conflicts.Add("A identificação da raiz (.stroot.json) foi removida. Restaure esse arquivo antes de sincronizar.");
            var registration = repository.FindRoot(marker.RootId);
            if (registration is { } registered && (registered.BoardId != board.Id ||
                (!string.Equals(registered.Path, root, StringComparison.OrdinalIgnoreCase) && files.Exists(registered.Path))))
                conflicts.Add("Esta raiz foi copiada de outro local ou pertence a outro quadro. Use uma pasta principal independente.");
            known = repository.KnownCardIds(marker.RootId, board.Id);
            var stack = new Stack<(string Path, int Depth)>(); stack.Push((root, 0));
            while (stack.TryPop(out var current))
            {
                token.ThrowIfCancellationRequested();
                foreach (var entry in files.EnumerateEntries(current.Path))
                {
                    token.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(entry);
                    var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '\\');
                    if (files.IsReparsePoint(entry)) { conflicts.Add($"Link simbólico/junction não será atravessado: {relative}"); continue; }
                    if (name.StartsWith(SafeLocalFileSystem.StagePrefix, StringComparison.OrdinalIgnoreCase) || name.Contains(".json.tmp-", StringComparison.OrdinalIgnoreCase))
                    { conflicts.Add($"Há uma operação interrompida a revisar: {relative}"); continue; }
                    if (!files.IsDirectory(entry))
                    { if (current.Depth == 1) occupied.Add(relative); continue; }
                    if (current.Depth == 1) occupied.Add(relative);
                    var metaPath = Path.Combine(entry, SafeLocalFileSystem.CardMetadataName);
                    if (files.Exists(metaPath))
                    {
                        if (!hasMarker) conflicts.Add("Foram encontrados cards identificados, mas falta a identificação da raiz (.stroot.json).");
                        var metadata = files.ReadMetadata<CardFolderMetadata>(root, metaPath);
                        ValidateMetadata(metadata, marker, null);
                        if (current.Depth != 1) conflicts.Add($"Pasta identificada fora da estrutura Lista \\ Card: {relative}");
                        else actual.Add(new(metadata.CardId, relative));
                    }
                    stack.Push((entry, current.Depth + 1));
                }
            }
            foreach (var card in desired.Cards)
            {
                files.ResolveCardPath(root, card.ExpectedDirectory);
                var parent = Path.Combine(root, card.ExpectedDirectory.Split('\\')[0]);
                if (files.Exists(parent) && !files.IsDirectory(parent)) conflicts.Add($"Um arquivo ocupa o nome da lista: {Path.GetFileName(parent)}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SyncValidationException)
        { conflicts.Add(ex is SyncValidationException ? ex.Message : "Não foi possível ler toda a pasta principal. Verifique o disco, as permissões e arquivos em uso."); }
        var state = new ActualState(available, actual, occupied.ToArray(), known);
        var plan = engine.BuildPlan(desired, state);
        return new(root, marker, board, memberId, desired, state, plan with { Conflicts = plan.Conflicts.Concat(conflicts).Distinct().ToArray() });
    }

    private SyncExecutionResult Apply(PreparedFolderSync approved, BoardSnapshot latest, string memberId, IProgress<SyncExecutionProgress>? progress, CancellationToken token)
    {
        if (!approved.Plan.CanExecute) throw new SyncValidationException("Existem conflitos. Revise-os antes de aplicar.");
        token.ThrowIfCancellationRequested();
        if (latest.Id != approved.Board.Id || memberId != approved.MemberId)
            throw new SyncValidationException("O quadro ou a conta mudou. Verifique novamente antes de aplicar.");
        files.ValidateRoot(approved.RootPath);
        using var rootLock = files.AcquireLock(approved.RootPath);
        var fresh = Prepare(approved.RootPath, latest, memberId, approved.Marker, token);
        if (!fresh.Plan.CanExecute || fresh.Marker != approved.Marker || !SameDesired(approved.Desired, fresh.Desired) || !SamePlan(approved, fresh))
            throw new SyncValidationException("O Trello ou as pastas mudaram desde a prévia. Clique em Verificar alterações e revise o novo plano.");
        if (fresh.Plan.Operations.Count == 0) return new("Completed", 0, "As pastas já estão organizadas.");
        var markerPath = Path.Combine(fresh.RootPath, SafeLocalFileSystem.RootMetadataName);
        if (!files.Exists(markerPath)) files.WriteMetadata(fresh.RootPath, markerPath, fresh.Marker, false);
        ValidateMarker(files.ReadMetadata<SyncRootMarker>(fresh.RootPath, markerPath), fresh.Board.Id);
        var syncId = repository.BeginRun(fresh);
        var completed = 0; var created = 0; var moved = 0; var renamed = 0;
        var status = "Completed"; var message = "Pastas organizadas com sucesso.";
        try
        {
            var cards = latest.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            foreach (var op in fresh.Plan.Operations)
            {
                token.ThrowIfCancellationRequested();
                if (files.ReadMetadata<SyncRootMarker>(fresh.RootPath, markerPath) != fresh.Marker)
                    throw new SyncValidationException("A identificação da pasta principal mudou. A execução foi interrompida.");
                var destination = files.ResolveCardPath(fresh.RootPath, op.Destination);
                if (files.Exists(destination)) throw new SyncValidationException($"Destino passou a estar ocupado: {op.Destination}");
                if (op.Source is not null)
                {
                    var from = files.ResolveCardPath(fresh.RootPath, op.Source);
                    ValidateMetadata(files.ReadMetadata<CardFolderMetadata>(fresh.RootPath, Path.Combine(from, SafeLocalFileSystem.CardMetadataName)), fresh.Marker, op.CardId);
                }
                repository.MarkApplying(syncId, op.CardId);
                var card = cards[op.CardId];
                var metadata = new CardFolderMetadata(1, fresh.Marker.RootId, latest.Id, card.Id, card.ListId, card.Name, card.Url, DateTimeOffset.UtcNow);
                if (op.Type == SyncOperationType.CreateDirectory)
                { files.CreateCardFolder(fresh.RootPath, op.Destination, metadata); created++; }
                else
                {
                    files.MoveCardFolder(fresh.RootPath, op.Source!, op.Destination);
                    if (op.Type is SyncOperationType.MoveDirectory or SyncOperationType.MoveAndRename) moved++;
                    if (op.Type is SyncOperationType.RenameDirectory or SyncOperationType.MoveAndRename) renamed++;
                    files.WriteMetadata(fresh.RootPath, Path.Combine(destination, SafeLocalFileSystem.CardMetadataName), metadata, true);
                }
                repository.CompleteOperation(syncId, fresh, op);
                completed++;
                progress?.Report(new(completed, fresh.Plan.Operations.Count, op.Destination));
            }
        }
        catch (OperationCanceledException) { status = "Cancelled"; message = "Sincronização cancelada entre operações. As pastas já concluídas foram preservadas."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SyncValidationException or Microsoft.Data.Sqlite.SqliteException)
        { status = "Failed"; message = ex is SyncValidationException ? ex.Message : "A sincronização foi interrompida. Verifique o disco, as permissões e arquivos abertos; depois confira o plano novamente."; }
        repository.FinishRun(syncId, status, created, moved, renamed, message);
        return new(status, completed, $"{message} {created} criadas, {moved} movidas, {renamed} renomeadas.");
    }

    public string VerifiedCardPath(PreparedFolderSync preview, string cardId)
    {
        var actual = preview.Actual.Cards.SingleOrDefault(c => c.CardId == cardId) ?? throw new SyncValidationException("A pasta desta demanda ainda não foi localizada.");
        var path = files.ResolveCardPath(preview.RootPath, actual.ActualDirectory);
        if (files.ReadMetadata<SyncRootMarker>(preview.RootPath, Path.Combine(preview.RootPath, SafeLocalFileSystem.RootMetadataName)) != preview.Marker)
            throw new SyncValidationException("A identificação da raiz mudou. Verifique novamente.");
        ValidateMetadata(files.ReadMetadata<CardFolderMetadata>(preview.RootPath, Path.Combine(path, SafeLocalFileSystem.CardMetadataName)), preview.Marker, cardId);
        return path;
    }
    private static bool SameDesired(DesiredState a, DesiredState b) => a.Cards.OrderBy(c => c.CardId, StringComparer.Ordinal).SequenceEqual(b.Cards.OrderBy(c => c.CardId, StringComparer.Ordinal));
    private static bool SamePlan(PreparedFolderSync a, PreparedFolderSync b) =>
        a.Plan.Operations.OrderBy(o => o.CardId, StringComparer.Ordinal).SequenceEqual(b.Plan.Operations.OrderBy(o => o.CardId, StringComparer.Ordinal)) &&
        a.Actual.Cards.OrderBy(c => c.CardId, StringComparer.Ordinal).SequenceEqual(b.Actual.Cards.OrderBy(c => c.CardId, StringComparer.Ordinal)) &&
        a.Actual.OccupiedDirectories.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(b.Actual.OccupiedDirectories.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    private static void ValidateMarker(SyncRootMarker marker, string boardId)
    {
        if (marker.Version != 1 || !Guid.TryParseExact(marker.RootId, "N", out _) || marker.BoardId != boardId)
            throw new SyncValidationException("A identificação da raiz é inválida ou pertence a outro quadro. Escolha uma pasta independente para cada quadro.");
    }
    private static void ValidateMetadata(CardFolderMetadata metadata, SyncRootMarker root, string? cardId)
    {
        if (metadata.Version != 1 || metadata.RootId != root.RootId || metadata.BoardId != root.BoardId ||
            string.IsNullOrWhiteSpace(metadata.CardId) || string.IsNullOrWhiteSpace(metadata.ListId) || metadata.CardName is null || metadata.CardUrl is null ||
            (cardId is not null && metadata.CardId != cardId))
            throw new SyncValidationException("Identificação de card inválida, duplicada ou pertencente a outra raiz. Revise os metadados da pasta.");
    }
}
