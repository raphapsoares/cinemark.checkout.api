namespace cinemark.checkout.shared.data;

/// <summary>
/// Configuração do banco (seção "Database" do appsettings.json). Fora de Development, o arquivo SQLite precisa já
/// existir: a aplicação nunca cria nem migra o esquema.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Caminho do arquivo SQLite existente.</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>Quanto tempo uma conexão espera por um lock de outra conexão.</summary>
    public int BusyTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Só em Development: apaga e recria o banco a cada start, a partir de schema.sql e seed.sql da mesma pasta do
    /// arquivo .db (database/). Ignorado nos outros ambientes.
    /// </summary>
    public bool ResetOnStartup { get; set; }
}
