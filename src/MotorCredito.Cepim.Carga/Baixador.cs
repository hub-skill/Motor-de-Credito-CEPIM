using System.Globalization;
using System.IO.Compression;

namespace MotorCredito.Cepim.Carga;

/// <summary>
/// Baixa do Portal da Transparência o arquivo mais recente do CEPIM (diário, em ZIP). O Portal só mantém o arquivo mais
/// recente e o do dia pode ainda não existir (403): recua até 7 dias. Segue o redirecionamento (302) do portal.
/// </summary>
public sealed class Baixador(HttpClient http)
{
    private const string Endereco = "https://portaldatransparencia.gov.br/download-de-dados/cepim";

    public async Task<string> BaixarAsync(string pastaDestino, DateOnly hoje, CancellationToken cancellationToken)
    {
        foreach (var periodo in Enumerable.Range(0, 8).Select(i => hoje.AddDays(-i).ToString("yyyyMMdd", CultureInfo.InvariantCulture)))
        {
            using var resposta = await http.GetAsync(new Uri($"{Endereco}/{periodo}"), cancellationToken).ConfigureAwait(false);
            if (!resposta.IsSuccessStatusCode)
            {
                continue; // arquivos antigos e o do dia ainda não publicado respondem 403
            }

            using var memoria = new MemoryStream();
            await resposta.Content.CopyToAsync(memoria, cancellationToken).ConfigureAwait(false);
            memoria.Position = 0;
            using var zip = new ZipArchive(memoria, ZipArchiveMode.Read);
            var entrada = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            if (entrada is null)
            {
                continue;
            }

            Directory.CreateDirectory(pastaDestino);
            // Só o nome do arquivo: nunca usa o caminho que vem dentro do zip.
            var destino = Path.Combine(pastaDestino, Path.GetFileName(entrada.Name));
            using (var origem = await entrada.OpenAsync(cancellationToken).ConfigureAwait(false))
            using (var arquivo = File.Create(destino))
            {
                await origem.CopyToAsync(arquivo, cancellationToken).ConfigureAwait(false);
            }

            return destino;
        }

        throw new InvalidOperationException("Nenhum arquivo CEPIM disponível no Portal.");
    }
}
