using Npgsql;
using RinhaApi.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using RinhaApi.Request;
using RinhaApi.Response;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = "Host=localhost;Username=rinha;Password=rinha;Database=rinha";

builder.Services.AddSingleton<DB>(provider => new DB(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


app.MapGet("clientes/{id}/extrato", async (int id) =>
{
    var result = new Extrato(){
        Saldo = new SaldoResponse() {
           DataExtrato = DateTime.Now,
           Limite = 0,
           Total = 0
        },
        UltimasTransacoes = new List<Transacao>()
    };
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<DB>();
        var conn = db.GetConnection();

        var selectQuery = "select valor, descricao, realizadaem from transacao where idcliente = @idcliente LIMIT 10";
        using (var command = new NpgsqlCommand(selectQuery, conn))
        {
            command.Parameters.AddWithValue("idcliente", id);
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (reader.Read())
                {
                    var transacao = new Transacao()
                    {
                        Valor = reader.GetInt32(0),
                        Descricao = reader.GetString(1),
                        Tipo = reader.GetInt32(0) <= 0 ? "D" : "C"
                    };
                    result.UltimasTransacoes.Add(transacao);
                }
            }
        }
    }
    return Results.Ok(result);
});


app.MapPost("clientes/{id}/transacoes", async (int id, Transacao transacao) =>
{
    try
    {
        using (var scope = app.Services.CreateScope())
        {
            if (string.IsNullOrWhiteSpace(transacao.Descricao)) {
                return Results.BadRequest("Descricao inválida");
            }
            if (string.IsNullOrWhiteSpace(transacao.Tipo)) {
                return Results.BadRequest("Tipo inválido");
            }
            if (!(transacao.Tipo.Equals("d",StringComparison.InvariantCultureIgnoreCase) ||
            transacao.Tipo.Equals("c",StringComparison.InvariantCultureIgnoreCase))) {
                return Results.BadRequest("Tipo inválido");
            }
            if (transacao.Valor <= 0) {
                return Results.BadRequest("Valor inválido");
            }

            var db = scope.ServiceProvider.GetRequiredService<DB>();
            var conn = db.GetConnection();

            var insertQuery = "insert into transacao(valor, descricao, idcliente, realizadaem) values (@valor, @descricao, @idcliente, CURRENT_DATE)";
            using (var command = new NpgsqlCommand(insertQuery, conn))
            {
                command.Parameters.AddWithValue("idcliente", id);
                command.Parameters.AddWithValue("valor", transacao.Tipo.Equals("d",StringComparison.InvariantCultureIgnoreCase) ? transacao.Valor * -1 : transacao.Valor);
                command.Parameters.AddWithValue("descricao", transacao.Descricao);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                Console.WriteLine($"{rowsAffected.ToString()} row(s) inserted.");
                var response = new TransacaoResponse()
                {
                    Limite = 10000,
                    Saldo = 100
                };
                return Results.Ok<TransacaoResponse>(response);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error connecting to PostgreSQL: " + ex.Message);
        return Results.StatusCode(500);
    }
});



app.Run();

return 0;
