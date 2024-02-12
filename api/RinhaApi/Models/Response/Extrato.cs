using RinhaApi.Request;

namespace RinhaApi.Response;

public class Extrato
{
    public Extrato()
    {
        Saldo = new SaldoResponse();
        UltimasTransacoes = new List<Transacao>();
    }
    public SaldoResponse Saldo { get; set; }
    public List<Transacao> UltimasTransacoes { get; set; }

}

public class SaldoResponse
{
    public int Total { get; set; }
    public DateTime DataExtrato { get; set; }
    public int Limite { get; set; }

    public bool ClienteExiste { get; set; }
}