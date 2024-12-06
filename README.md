using System;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace JobTestingNamespace
{
    /// <summary>
    /// Represents a complex database job for testing DLL load and unload services
    /// </summary>
    public class ComplexDatabaseJob : IJob
    {
        private readonly string _connectionString;
        private readonly int _batchSize;
        private readonly string _tableName;

        /// <summary>
        /// Initializes a new instance of the ComplexDatabaseJob
        /// </summary>
        /// <param name="connectionString">Database connection string</param>
        /// <param name="batchSize">Number of records to process in each batch</param>
        /// <param name="tableName">Name of the table to process</param>
        public ComplexDatabaseJob(
            string connectionString, 
            int batchSize = 100, 
            string tableName = "JobProcessingTable")
        {
            _connectionString = connectionString;
            _batchSize = batchSize;
            _tableName = tableName;
        }

        /// <summary>
        /// Executes the job asynchronously
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the job</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Validate connection
                await ValidateDatabaseConnectionAsync();

                // Fetch and process records in batches
                await ProcessRecordsInBatchesAsync(cancellationToken);

                // Perform cleanup operations
                await CleanupProcessedRecordsAsync();
            }
            catch (Exception ex)
            {
                // Log or handle exceptions
                Console.WriteLine($"Job execution failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Validates the database connection
        /// </summary>
        private async Task ValidateDatabaseConnectionAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the table exists
                using (var command = new SqlCommand(
                    $"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{_tableName}') " +
                    $"CREATE TABLE {_tableName} (" +
                    "Id INT IDENTITY(1,1) PRIMARY KEY, " +
                    "Data NVARCHAR(MAX), " +
                    "ProcessedAt DATETIME, " +
                    "Status NVARCHAR(50))", connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Processes records in batches
        /// </summary>
        private async Task ProcessRecordsInBatchesAsync(CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Fetch unprocessed records
                string fetchQuery = $@"
                    SELECT TOP {_batchSize} Id, Data 
                    FROM {_tableName} 
                    WHERE Status IS NULL OR Status != 'Processed'
                    ORDER BY Id";

                using (var fetchCommand = new SqlCommand(fetchQuery, connection))
                using (var reader = await fetchCommand.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        // Simulate processing
                        int id = reader.GetInt32(0);
                        string data = reader.GetString(1);

                        await ProcessSingleRecordAsync(connection, id, data, cancellationToken);

                        // Optional: Add a small delay to simulate processing time
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
        }

        /// <summary>
        /// Processes a single record
        /// </summary>
        private async Task ProcessSingleRecordAsync(
            SqlConnection connection, 
            int id, 
            string data, 
            CancellationToken cancellationToken)
        {
            using (var updateCommand = new SqlCommand(
                $"UPDATE {_tableName} " +
                "SET Status = 'Processed', " +
                "ProcessedAt = @ProcessedAt " +
                "WHERE Id = @Id", connection))
            {
                updateCommand.Parameters.AddWithValue("@ProcessedAt", DateTime.UtcNow);
                updateCommand.Parameters.AddWithValue("@Id", id);

                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Cleans up processed records
        /// </summary>
        private async Task CleanupProcessedRecordsAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Optional: Delete records older than 30 days
                using (var deleteCommand = new SqlCommand(
                    $"DELETE FROM {_tableName} " +
                    "WHERE Status = 'Processed' " +
                    "AND ProcessedAt < @OldDate", connection))
                {
                    deleteCommand.Parameters.AddWithValue("@OldDate", DateTime.UtcNow.AddDays(-30));
                    await deleteCommand.ExecuteNonQueryAsync();
                }
            }
        }
    }

    /// <summary>
    /// Interface for job execution
    /// </summary>
    public interface IJob
    {
        /// <summary>
        /// Executes the job asynchronously
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the job</param>
        /// <returns>Task representing the asynchronous operation</returns>
        Task ExecuteAsync(CancellationToken cancellationToken);
    }
}
