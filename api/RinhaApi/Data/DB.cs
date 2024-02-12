using Npgsql;
namespace RinhaApi.Data;

public class DB : IDisposable
{
    private readonly NpgsqlConnection connection;

    public DB(string connectionString)
    {
        connection = new NpgsqlConnection(connectionString);
        connection.Open();
    }

    public NpgsqlConnection GetConnection()
    {
        return connection;
    }

    public void Dispose()
    {
        connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}
