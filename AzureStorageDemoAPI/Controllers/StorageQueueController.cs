using Azure;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;

namespace AzureStorageDemoAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StorageQueueController : ControllerBase
    {
        private readonly QueueServiceClient _queueService;
        private readonly ILogger<StorageQueueController> _logger;

        public StorageQueueController(QueueServiceClient queueService, ILogger<StorageQueueController> logger)
        {
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("queues/{queueName}/create")]
        public async Task<IActionResult> CreateQueue(string queueName)
        {
            if (string.IsNullOrWhiteSpace(queueName)) return BadRequest("queueName is required.");

            try
            {
                var queueClient = _queueService.GetQueueClient(queueName);
                var response = await queueClient.CreateIfNotExistsAsync();
                _logger.LogInformation("CreateIfNotExists called for queue '{QueueName}' - created={Created}", queueName, response != null);
                return Ok(new { Queue = queueName, Created = response != null });
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error creating queue '{QueueName}'", queueName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("queues/{queueName}/messages")]
        public async Task<IActionResult> SendMessage(string queueName, [FromBody] string message)
        {
            if (string.IsNullOrWhiteSpace(queueName)) return BadRequest("queueName is required.");
            if (string.IsNullOrEmpty(message)) return BadRequest("Message body required.");

            try
            {
                var queueClient = _queueService.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();
                var response = await queueClient.SendMessageAsync(message);
                _logger.LogInformation("Enqueued message to '{QueueName}' MessageId={MessageId}", queueName, response.Value.MessageId);
                return Ok(new { Queue = queueName, MessageId = response.Value.MessageId });
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error sending message to queue '{QueueName}'", queueName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("queues/{queueName}/peek")]
        public async Task<IActionResult> PeekMessages(string queueName, [FromQuery] int maxMessages = 5)
        {
            if (string.IsNullOrWhiteSpace(queueName)) return BadRequest("queueName is required.");
            try
            {
                var queueClient = _queueService.GetQueueClient(queueName);
                var response = await queueClient.PeekMessagesAsync(maxMessages);
                var result = new List<object>();
                foreach (var msg in response.Value)
                {
                    result.Add(new { msg.MessageId, msg.InsertedOn, msg.ExpiresOn, MessageText = msg.MessageText });
                }

                _logger.LogInformation("Peeked {Count} messages from '{QueueName}'", result.Count, queueName);
                return Ok(result);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error peeking messages from queue '{QueueName}'", queueName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("queues/{queueName}/receive")]
        public async Task<IActionResult> ReceiveMessages(string queueName, [FromQuery] int maxMessages = 1, [FromQuery] int visibilityTimeoutSeconds = 30)
        {
            if (string.IsNullOrWhiteSpace(queueName)) return BadRequest("queueName is required.");
            try
            {
                var queueClient = _queueService.GetQueueClient(queueName);
                var response = await queueClient.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(visibilityTimeoutSeconds));
                var result = new List<object>();
                foreach (var msg in response.Value)
                {
                    result.Add(new
                    {
                        msg.MessageId,
                        msg.PopReceipt,
                        msg.InsertedOn,
                        msg.ExpiresOn,
                        MessageText = msg.MessageText
                    });
                }

                _logger.LogInformation("Received {Count} messages from '{QueueName}'", result.Count, queueName);
                return Ok(result);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error receiving messages from queue '{QueueName}'", queueName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("queues/{queueName}/messages/{messageId}/{popReceipt}")]
        public async Task<IActionResult> DeleteMessage(string queueName, string messageId, string popReceipt)
        {
            if (string.IsNullOrWhiteSpace(queueName) || string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(popReceipt))
                return BadRequest("queueName, messageId and popReceipt are required.");

            try
            {
                var queueClient = _queueService.GetQueueClient(queueName);
                await queueClient.DeleteMessageAsync(messageId, popReceipt);
                _logger.LogInformation("Deleted message {MessageId} from queue '{QueueName}'", messageId, queueName);
                return NoContent();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error deleting message {MessageId} from queue '{QueueName}'", messageId, queueName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("queues/{queueName}/properties")]
        public async Task<IActionResult> GetProperties(string queueName)
        {
            if (string.IsNullOrWhiteSpace(queueName)) return BadRequest("queueName is required.");

            try
            {
                var queueClient = _queueService.GetQueueClient(queueName);
                var props = await queueClient.GetPropertiesAsync();
                _logger.LogInformation("Fetched properties for queue '{QueueName}' ApproximateMessagesCount={Count}", queueName, props.Value.ApproximateMessagesCount);
                return Ok(new { ApproximateMessagesCount = props.Value.ApproximateMessagesCount });
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error getting properties for queue '{QueueName}'", queueName);
                return StatusCode(500, ex.Message);
            }
        }
    }
}
