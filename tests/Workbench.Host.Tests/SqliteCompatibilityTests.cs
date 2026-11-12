namespace Workbench.Host.Tests;

using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteCompatibilityTests
{
    [Fact]
    public void NativeSqliteCommitsReopensAndRollsBackSyntheticData()
    {
        var directory = Directory.CreateTempSubdirectory("drone-workbench-sqlite-");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory.FullName, "synthetic.db"),
            Pooling = false,
        }.ToString();
        try
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var schema = connection.CreateCommand();
                schema.CommandText = "CREATE TABLE probe (value TEXT NOT NULL);";
                schema.ExecuteNonQuery();
                using var transaction = connection.BeginTransaction();
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO probe (value) VALUES ($value);";
                insert.Parameters.AddWithValue("$value", "synthetic-committed");
                Assert.Equal(1, insert.ExecuteNonQuery());
                transaction.Commit();
            }

            using (var reopened = new SqliteConnection(connectionString))
            {
                reopened.Open();
                using (var transaction = reopened.BeginTransaction())
                {
                    using var insert = reopened.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO probe (value) VALUES ('synthetic-rolled-back');";
                    insert.ExecuteNonQuery();
                    transaction.Rollback();
                }

                using var select = reopened.CreateCommand();
                select.CommandText = "SELECT value FROM probe;";
                using var reader = select.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("synthetic-committed", reader.GetString(0));
                Assert.False(reader.Read());
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
