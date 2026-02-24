
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Files.Shares;
using Azure.Storage.Queues;
using AzureStorageDemoAPI.Models;

var builder = WebApplication.CreateBuilder(args);

// Load storage connection string (try ConnectionStrings:AzureStorage, AzureStorage:ConnectionString, then env var)
var storageConnectionString =
    builder.Configuration.GetConnectionString("AzureStorage")
    ?? builder.Configuration["AzureStorage:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Azure Storage connection string not configured. Set ConnectionStrings:AzureStorage or AZURE_STORAGE_CONNECTION_STRING.");


// Register Azure Storage clients in DI
builder.Services.AddSingleton<TableServiceClient>(_ => new TableServiceClient(storageConnectionString));
builder.Services.AddSingleton<QueueServiceClient>(_ => new QueueServiceClient(storageConnectionString));
builder.Services.AddSingleton<ShareServiceClient>(_ => new ShareServiceClient(storageConnectionString));
builder.Services.AddSingleton<BlobServiceClient>(_ => new BlobServiceClient(storageConnectionString));
builder.Services.AddSingleton(new StorageConfiguration(storageConnectionString));
builder.Services.AddApplicationInsightsTelemetry();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();


//app.MapPost("/queues/{name}/messages", async (QueueServiceClient queueService, string name, HttpRequest request) =>
//{
//    using var sr = new StreamReader(request.Body);
//    var message = await sr.ReadToEndAsync();
//    if (string.IsNullOrEmpty(message)) return Results.BadRequest("Message body required.");
//    var queueClient = queueService.GetQueueClient(name);
//    await queueClient.CreateIfNotExistsAsync();
//    await queueClient.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message)));
//    return Results.Ok(new { Queue = name, Enqueued = true });
//});

//app.MapPost("/tables/{name}/create", async (TableServiceClient tableService, string name) =>
//{
//    var tableClient = tableService.GetTableClient(name);
//    await tableClient.CreateIfNotExistsAsync();
//    return Results.Ok(new { Table = name, Created = true });
//});

//app.MapPost("/shares/{name}/create", async (ShareServiceClient shareService, string name) =>
//{
//    var shareClient = shareService.GetShareClient(name);
//    var response = await shareClient.CreateIfNotExistsAsync();
//    return Results.Ok(new { Share = name, Created = response != null });
//});

app.Run();