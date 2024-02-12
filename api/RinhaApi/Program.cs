using Npgsql;
using RinhaApi.Request;
using RinhaApi.Response;
using Microsoft.AspNetCore.HttpLogging;
using System.Text.Json;


var builder = WebApplication.CreateSlimBuilder(args);
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());


builder.Services.AddHttpLogging(o =>
{
    o.CombineLogs = true;
    o.LoggingFields = HttpLoggingFields.All;
});
// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});


var connectionString = builder.Configuration.GetConnectionString("rinha");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("No connection string found.");
    return 1;
}

//var connectionString = "Host=localhost;Username=rinha;Password=rinha;Database=rinha";

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
//dataSourceBuilder.UseLoggerFactory(loggerFactory);
//dataSourceBuilder.EnableParameterLogging();
await using var datasource = dataSourceBuilder.Build();



var app = builder.Build();

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//app.UseSwagger();
//app.UseSwaggerUI();
//}

//app.UseHttpsRedirection();

SemaphoreSlim semaphore = new SemaphoreSlim(10);

app.MapGet(
    "clientes/{id}/extrato",
    async (int id) =>
    {
        await semaphore.WaitAsync();
        try
        {
            var result = new Extrato()
            {
                Saldo = new SaldoResponse()
                {
                    DataExtrato = DateTime.Now,
                    Limite = 0,
                    Total = 0
                },
                UltimasTransacoes = []
            };
            var saldo = await GetSaldo(id);
            if (!saldo.ClienteExiste)
            {
                return Results.StatusCode(404);
            }

            result.Saldo = saldo;

            var selectQuery =
                "select valor, descricao, realizadaem from transacao where idcliente = $1 and realizadaem <= $2 order by realizadaem desc LIMIT 10";
            await using var command = datasource.CreateCommand(selectQuery);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(DateTime.UtcNow);


            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var transacao = new Transacao()
                {
                    Valor = reader.GetInt32(0) <= 0 ? reader.GetInt32(0) * -1 : reader.GetInt32(0),
                    Descricao = reader.GetString(1),
                    Tipo = reader.GetInt32(0) <= 0 ? "d" : "c"
                };
                result.UltimasTransacoes.Add(transacao);
            };
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Extrato] - Error: {ex.Message}");
            return Results.StatusCode(501);
        }
        finally
        {
            // Release the permit when the operation is complete
            semaphore.Release();
        }
    }
);

app.MapPost(
    "clientes/{id}/transacoes",
    async (int id, Transacao transacao) =>
    {
        await semaphore.WaitAsync();
        // var request = $"id:{id},descricao:{transacao.Descricao},tipo:{transacao.Tipo},valor:{transacao.Valor?.ToString()}";
        try
        {
            if (string.IsNullOrWhiteSpace(transacao.Descricao) || transacao.Descricao.Length > 10)
            {
                // Console.WriteLine($"request:{request}, response: 422, Descricao");
                return Results.StatusCode(422);
            }
            if (string.IsNullOrWhiteSpace(transacao.Tipo))
            {
                // Console.WriteLine($"request:{request}, response: 422, Tipo");
                return Results.StatusCode(422);
            }
            if (
                !(
                    transacao.Tipo.Equals("d", StringComparison.InvariantCultureIgnoreCase)
                    || transacao.Tipo.Equals("c", StringComparison.InvariantCultureIgnoreCase)
                )
            )
            {
                //Console.WriteLine($"request:{request}, response: 422, Tipo");
                return Results.StatusCode(422);
            }

            //if (string.IsNullOrEmpty(transacao.Valor)){
            //    Console.WriteLine($"[Transacao] valor vazio");
            //    return Results.StatusCode(422);
            //}

            if (int.TryParse(transacao.Valor?.ToString(), out var valors) is false)
            {
                //Console.WriteLine($"request:{request}, response: 422, Valor");
                return Results.StatusCode(422);
            }

            if (valors <= 0)
            {
                //Console.WriteLine($"request:{request}, response: 422, Valor");
                return Results.StatusCode(422);
            }

            var valor = transacao.Tipo.Equals("d", StringComparison.InvariantCultureIgnoreCase)
                ? valors * -1
                : valors;
            var insertQuery = "select criartransacao($1, $2, $3)";
            await using var command = datasource.CreateCommand(insertQuery);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(valor);
            command.Parameters.AddWithValue(transacao.Descricao);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new Exception("Failed to get result from criartransacao");
            var record = reader.GetFieldValue<object[]>(0);
            if (record.Length == 1)
            {
                var code = (int)record[0];
                if (code == -1)
                {
                    // Console.WriteLine($"request:{request}, response: 422, Cliente Nao Existe");
                    return Results.StatusCode(404);
                }
                else if (code == -2)
                {
                    //Console.WriteLine($"request:{request}, response: 422, Limite");
                    return Results.StatusCode(422);
                }
                else
                    throw new Exception("invalid return code from criartransacao");
            }

            var response = new TransacaoResponse() { Limite = (int)record[1] * -1, Saldo = ((int)record[0]) };
            //Console.WriteLine($"request:{request}, response: 200, limite:{response.Limite},saldo:{response.Saldo}");
            return Results.Ok(response);

        }
        catch (Exception ex)
        {
            Console.WriteLine("Error =>" + ex.Message);
            return Results.StatusCode(500);
        }
        finally
        {
            // Release the permit when the operation is complete
            semaphore.Release();
        }
    }
);
async Task<SaldoResponse> GetSaldo(int id)
{
    var response = new SaldoResponse()
    {
        DataExtrato = DateTime.Now,
        Limite = 0,
        Total = 0
    };
    var command = datasource.CreateCommand("select saldo, limite from cliente where id = @id");
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        response.Total = reader.GetInt32(0);
        response.Limite = reader.GetInt32(1) * -1;
        response.ClienteExiste = true;
    }
    else
    {
        response.Total = 0;
        response.Limite = 0;
        response.ClienteExiste = false;
    }
    return response;
}

app.Run();

return 0;
