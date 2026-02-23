namespace AzureStorageDemoAPI.Models
{
    /// <summary>
    /// Simple container for the storage connection string registered in DI.
    /// </summary>
    public sealed class StorageConfiguration
    {
        public string ConnectionString { get; }

        public StorageConfiguration(string connectionString)
        {
            ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }
    }
}