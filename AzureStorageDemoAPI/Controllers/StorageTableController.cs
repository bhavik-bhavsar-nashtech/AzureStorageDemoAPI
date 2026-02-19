using Azure;
using Azure.Data.Tables;
using AzureStorageDemoAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AzureStorageDemoAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StorageTableController : ControllerBase
    {
        private readonly TableServiceClient _tableService;
        private readonly ILogger<StorageTableController> _logger;

        public StorageTableController(TableServiceClient tableService, ILogger<StorageTableController> logger)
        {
            _tableService = tableService ?? throw new ArgumentNullException(nameof(tableService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Create table if not exists
        [HttpPost("tables/{tableName}/create")]
        public async Task<IActionResult> CreateTable(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return BadRequest("tableName is required.");

            try
            {
                var tableClient = _tableService.GetTableClient(tableName);
                var response = await tableClient.CreateIfNotExistsAsync();
                var created = response != null;
                _logger.LogInformation("CreateIfNotExists called for table '{TableName}' - created={Created}", tableName, created);
                return Ok(new { Table = tableName, Created = created });
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error creating table '{TableName}'", tableName);
                return StatusCode(500, ex.Message);
            }
        }

        // Insert a new entity.
        [HttpPost("tables/{tableName}/entities")]
        public async Task<IActionResult> AddEntity(string tableName, [FromBody] StorageEntity entity)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return BadRequest("tableName is required.");
            if (entity == null) return BadRequest("Entity body is required.");

            try
            {
                var tableClient = _tableService.GetTableClient(tableName);
                await tableClient.CreateIfNotExistsAsync();

                if (string.IsNullOrEmpty(entity.PartitionKey)) entity.PartitionKey = "default";
                if (string.IsNullOrEmpty(entity.RowKey)) entity.RowKey = Guid.NewGuid().ToString("N");

                await tableClient.AddEntityAsync(entity);
                _logger.LogInformation("Added entity to table '{TableName}' PartitionKey={PartitionKey} RowKey={RowKey}", tableName, entity.PartitionKey, entity.RowKey);

                return CreatedAtAction(nameof(GetEntity), new { tableName, partitionKey = entity.PartitionKey, rowKey = entity.RowKey }, entity);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error adding entity to table '{TableName}' PartitionKey={PartitionKey} RowKey={RowKey}", tableName, entity?.PartitionKey, entity?.RowKey);
                return StatusCode(500, ex.Message);
            }
        }

        // Get a single entity
        [HttpGet("tables/{tableName}/entities/{partitionKey}/{rowKey}")]
        public async Task<IActionResult> GetEntity(string tableName, string partitionKey, string rowKey)
        {
            var tableClient = _tableService.GetTableClient(tableName);
            try
            {
                var response = await tableClient.GetEntityAsync<StorageEntity>(partitionKey, rowKey);
                _logger.LogInformation("Fetched entity from table '{TableName}' PartitionKey={PartitionKey} RowKey={RowKey}", tableName, partitionKey, rowKey);
                return Ok(response.Value);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error fetching entity from table '{TableName}' PartitionKey={PartitionKey} RowKey={RowKey}", tableName, partitionKey, rowKey);
                return StatusCode(500, ex.Message);
            }
        }

        // Query entities by PartitionKey
        [HttpGet("tables/{tableName}/entities/partition/{partitionKey}")]
        public async Task<IActionResult> QueryByPartition(string tableName, string partitionKey)
        {
            var tableClient = _tableService.GetTableClient(tableName);
            var results = new List<StorageEntity>();

            try
            {
                await foreach (var entity in tableClient.QueryAsync<StorageEntity>(e => e.PartitionKey == partitionKey))
                {
                    results.Add(entity);
                }

                _logger.LogInformation("Queried {Count} entities from table '{TableName}' partition '{PartitionKey}'", results.Count, tableName, partitionKey);
                return Ok(results);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error querying entities from table '{TableName}' partition '{PartitionKey}'", tableName, partitionKey);
                return StatusCode(500, ex.Message);
            }
        }

        // Update an entity
        [HttpPut("tables/{tableName}/entities/{partitionKey}/{rowKey}")]
        public async Task<IActionResult> UpdateEntity(string tableName, string partitionKey, string rowKey, [FromBody] StorageEntity updated)
        {
            if (updated == null) return BadRequest("Entity body is required.");
            var tableClient = _tableService.GetTableClient(tableName);

            // Ensure keys match route
            updated.PartitionKey = partitionKey;
            updated.RowKey = rowKey;

            // Use Replace mode; use ETag if caller provided concurrency, otherwise use ETag.All to overwrite
            var etag = updated.ETag.Equals(default(ETag)) ? ETag.All : updated.ETag;

            try
            {
                await tableClient.UpdateEntityAsync(updated, etag, TableUpdateMode.Replace);
                _logger.LogInformation("Updated entity in table '{TableName}' PartitionKey={PartitionKey} RowKey={RowKey}", tableName, partitionKey, rowKey);
                return NoContent();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error updating entity in table '{TableName}' PartitionKey={PartitionKey} RowKey={RowKey}", tableName, partitionKey, rowKey);
                return StatusCode(500, ex.Message);
            }
        }

        // Delete an entity
        [HttpDelete("tables/{tableName}/entities/{partitionKey}/{rowKey}")]
        public async Task<IActionResult> DeleteEntity(string tableName, string partitionKey, string rowKey)
        {
            var tableClient = _tableService.GetTableClient(tableName);
            try
            {
                await tableClient.DeleteEntityAsync(partitionKey, rowKey);
                _logger.LogInformation("Deleted entity {RowKey} from table '{TableName}' partition '{PartitionKey}'", rowKey, tableName, partitionKey);
                return NoContent();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error deleting entity {RowKey} from table '{TableName}' partition '{PartitionKey}'", rowKey, tableName, partitionKey);
                return StatusCode(500, ex.Message);
            }
        }

        // Delete table
        [HttpDelete("tables/{tableName}/delete")]
        public async Task<IActionResult> DeleteTable(string tableName)
        {
            var tableClient = _tableService.GetTableClient(tableName);
            try
            {   
                await tableClient.DeleteAsync();
                _logger.LogInformation("Deleted table '{TableName}'", tableName);
                return NoContent();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error deleting table '{TableName}'", tableName);
                return StatusCode(500, ex.Message);
            }
        }
    }
}
