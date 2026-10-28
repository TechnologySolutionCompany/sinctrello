namespace SincTrello.App.ViewModels;

public sealed record DemandItem(string Ssc, string Client, string Description, string ListName,
    string Priority, int Attachments, string FolderStatus)
{
    public string Name => OriginalName ?? $"SSC {Ssc} - {Client}";
    public bool IsPending => FolderStatus.StartsWith("A ", StringComparison.Ordinal);
    public string? CardId { get; init; }
    public string? OriginalName { get; init; }
    public string? CardUrl { get; init; }
    public string FullDescription { get; init; } = "";
    public string Technician { get; init; } = "";
}

public sealed record ListSummary(string Name, int Count, double BarWidth);
public sealed record PlanRow(string Operation, string Demand, string Destination);

/// <summary>Examples from the visual reference. Never used as a Trello snapshot or disk state.</summary>
internal static class DemoData
{
    public static IReadOnlyList<DemandItem> Create() =>
    [
        new("1000101", "ESCRITÓRIO DEMO ALFA LTDA", "Relação de empréstimos", "Em Andamento", "Média", 16, "Exemplo"),
        new("1000102", "CONTÁBIL EXEMPLO BETA LTDA", "Importação de lançamentos contábeis via TXT", "Entrada de Demandas", "Alta", 4, "A criar"),
        new("1000103", "INDÚSTRIA FICTÍCIA GAMA LTDA", "Conjunto de dados de folha de pagamento", "Análise de Desenvolvimento", "Alta", 4, "A mover"),
        new("1000104", "DELTA DEMO CONTABILIDADE E CONSULTORIA", "Relatório de retenções de INSS por tomador", "Entrada de Demandas", "Alta", 4, "Exemplo"),
        new("1000105", "ÉPSILON CONTABILIDADE DEMO LTDA", "Importação de notas fiscais de serviço", "Entrada de Demandas", "Alta", 1, "Conflito"),
        new("1000106", "FULANO DE TAL DEMO", "Relatório de férias a vencer", "Entrada de Demandas", "Alta", 1, "A criar"),
        new("1000107", "CICLANO EXEMPLO DEMO", "Relatório gerencial por centro de custo", "Retorno de Análise", "Média", 2, "Exemplo"),
        new("1000108", "ZETA GESTÃO E ASSESSORIA DEMO", "Conjunto de dados do balancete comparativo", "Aguardando Retorno Financeiro", "Média", 2, "Exemplo"),
        new("1000109", "INDÚSTRIA FICTÍCIA GAMA LTDA", "Importação de plano de contas", "A Desenvolver (Pago)", "Média", 3, "A renomear"),
        new("1000110", "ETA CONTADORES ASSOCIADOS DEMO", "Relatório de apuração do Simples Nacional", "Aguardando Retorno Financeiro", "Alta", 3, "Exemplo"),
        new("1000111", "TETA COMÉRCIO DEMO LTDA", "Ajuste no layout de importação bancária", "Aguardando Retorno Financeiro", "Alta", 4, "Exemplo"),
        new("1000112", "IOTA ASSESSORIA CONTÁBIL DEMO", "Importação de saldos iniciais", "Em Implantação", "Média", 2, "Exemplo"),
        new("1000113", "CAPA CONTABILIDADE DEMO LTDA", "Relatório de provisões trabalhistas", "Concluído", "Baixa", 1, "Exemplo")
    ];

    public static IReadOnlyList<string> Lists { get; } =
    ["Entrada de Demandas", "Análise de Desenvolvimento", "Retorno de Análise", "Aguardando Retorno Financeiro",
        "A Desenvolver (Pago)", "A Desenvolver (Sem Custo)", "Em Andamento", "Em Implantação", "Concluído"];
}
