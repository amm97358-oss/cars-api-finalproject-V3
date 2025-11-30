using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;   // <= IMPORTANT

namespace Company.Function;

public class Cars_API_Basic
{
    private const string ApiKeyHeader = "x-api-key";

    // Reuse one SecretClient for both API key and SQL connection string
    private readonly SecretClient _secretClient;

    public Cars_API_Basic()
    {
        _secretClient = new SecretClient(
            new Uri("https://Finalkeyvault3121.vault.azure.net/"),  // your Key Vault URL
            new DefaultAzureCredential()
        );
    }

    public class Car
    {
        public Guid Id { get; set; }
        public string Manufacture { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public bool IsClassic { get; set; }
    }

    // Helper: get API key from Key Vault
    private bool Authorized(HttpRequest req)
    {
        var apiKeyValue = _secretClient.GetSecret("Final-Secret-Key").Value.Value;
        if (string.IsNullOrEmpty(apiKeyValue)) return false;

        return req.Headers.TryGetValue(ApiKeyHeader, out var v) && v == apiKeyValue;
    }

    // Helper: get SQL connection string from Key Vault
    private string GetSqlConnectionString()
    {
        return _secretClient.GetSecret("SqlConnectionString").Value.Value;
    }

    private static (bool ok, string? msg) Validate(Car? c)
    {
        if (c == null) return (false, "Body required");
        if (string.IsNullOrWhiteSpace(c.Manufacture)) return (false, "Manufacture required");
        if (string.IsNullOrWhiteSpace(c.Year)) return (false, "Year required");
        if (string.IsNullOrWhiteSpace(c.Model)) return (false, "Model required");
        if (string.IsNullOrWhiteSpace(c.Color)) return (false, "Color required");
        return (true, null);
    }

    // CREATE: insert into Cars table
    [Function("CreateCar")]
    public async Task<IActionResult> CreateCar(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cars")] HttpRequest req,
        FunctionContext context)
    {
        var logger = context.GetLogger("CreateCar");
        logger.LogInformation("CreateCar triggered.");

        if (!Authorized(req))
        {
            logger.LogWarning("Unauthorized request to POST /cars.");
            return new UnauthorizedResult();
        }

        Car? incoming;
        try
        {
            incoming = await JsonSerializer.DeserializeAsync<Car>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            logger.LogInformation("CreateCar request body deserialized.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CreateCar received invalid JSON.");
            return new BadRequestObjectResult(new { error = "Invalid JSON" });
        }

        var (ok, msg) = Validate(incoming);
        if (!ok)
        {
            logger.LogWarning("CreateCar validation failed: {Message}", msg);
            return new BadRequestObjectResult(new { error = msg });
        }

        incoming!.Id = Guid.NewGuid();
        logger.LogInformation("New car Id generated: {Id}", incoming.Id);

        var connString = GetSqlConnectionString();

        try
        {
            using (var conn = new SqlConnection(connString))
            {
                await conn.OpenAsync();
                logger.LogInformation("SQL connection opened for CreateCar.");

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Cars (Id, Manufacture, [Year], Model, Color)
                    VALUES (@Id, @Manufacture, @Year, @Model, @Color);";

                cmd.Parameters.AddWithValue("@Id", incoming.Id);
                cmd.Parameters.AddWithValue("@Manufacture", incoming.Manufacture);
                cmd.Parameters.AddWithValue("@Year", incoming.Year);
                cmd.Parameters.AddWithValue("@Model", incoming.Model);
                cmd.Parameters.AddWithValue("@Color", incoming.Color);
                // If your table has a default for IsClassic you don't need to insert it here

                var rows = await cmd.ExecuteNonQueryAsync();
                logger.LogInformation("CreateCar INSERT executed. Rows affected: {Rows}", rows);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateCar database error.");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        logger.LogInformation("CreateCar completed for Id {Id}", incoming.Id);
        return new CreatedResult($"/api/cars/{incoming.Id}", incoming);
    }

    // READ: get all cars from Cars table
    [Function("GetCars")]
    public async Task<IActionResult> GetCars(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cars")] HttpRequest req,
        FunctionContext context)
    {
        var logger = context.GetLogger("GetCars");
        logger.LogInformation("GetCars endpoint called.");

        if (!Authorized(req))
        {
            logger.LogWarning("Unauthorized request to GET /cars.");
            return new UnauthorizedResult();
        }

        var cars = new List<Car>();
        var connString = GetSqlConnectionString();

        try
        {
            using (var conn = new SqlConnection(connString))
            {
                await conn.OpenAsync();
                logger.LogInformation("SQL connection opened for GetCars.");

                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Manufacture, [Year], Model, Color, IsClassic FROM Cars;";

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    logger.LogInformation("GetCars SELECT executed, reading results...");
                    while (await reader.ReadAsync())
                    {
                        var car = new Car
                        {
                            Id = reader.GetGuid(0),
                            Manufacture = reader.GetString(1),
                            Year = reader.GetString(2),
                            Model = reader.GetString(3),
                            Color = reader.GetString(4),
                            IsClassic = reader.GetBoolean(5)
                        };
                        cars.Add(car);
                    }
                }
            }

            logger.LogInformation("GetCars returning {Count} cars.", cars.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetCars database error.");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return new OkObjectResult(cars);
    }

    // UPDATE: update a car row by Id
    [Function("UpdateCar")]
    public async Task<IActionResult> UpdateCar(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cars/{id:guid}")] HttpRequest req,
        Guid id,
        FunctionContext context)
    {
        var logger = context.GetLogger("UpdateCar");
        logger.LogInformation("UpdateCar triggered for Id {Id}.", id);

        if (!Authorized(req))
        {
            logger.LogWarning("Unauthorized request to PUT /cars/{Id}.", id);
            return new UnauthorizedResult();
        }

        Car? incoming;
        try
        {
            incoming = await JsonSerializer.DeserializeAsync<Car>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            logger.LogInformation("UpdateCar request body deserialized.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "UpdateCar received invalid JSON.");
            return new BadRequestObjectResult(new { error = "Invalid JSON" });
        }

        var (ok, msg) = Validate(incoming);
        if (!ok)
        {
            logger.LogWarning("UpdateCar validation failed: {Message}", msg);
            return new BadRequestObjectResult(new { error = msg });
        }

        var connString = GetSqlConnectionString();
        int rowsAffected;

        try
        {
            using (var conn = new SqlConnection(connString))
            {
                await conn.OpenAsync();
                logger.LogInformation("SQL connection opened for UpdateCar.");

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Cars
                    SET Manufacture = @Manufacture,
                        [Year] = @Year,
                        Model = @Model,
                        Color = @Color,
                        IsClassic = @IsClassic
                    WHERE Id = @Id;";

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Manufacture", incoming!.Manufacture);
                cmd.Parameters.AddWithValue("@Year", incoming.Year);
                cmd.Parameters.AddWithValue("@Model", incoming.Model);
                cmd.Parameters.AddWithValue("@Color", incoming.Color);
                cmd.Parameters.AddWithValue("@IsClassic", incoming.IsClassic);

                rowsAffected = await cmd.ExecuteNonQueryAsync();
                logger.LogInformation("UpdateCar UPDATE executed. Rows affected: {Rows}", rowsAffected);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateCar database error for Id {Id}.", id);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        if (rowsAffected == 0)
        {
            logger.LogWarning("UpdateCar found no record with Id {Id}.", id);
            return new NotFoundObjectResult(new { error = "Not found" });
        }

        incoming!.Id = id;
        logger.LogInformation("UpdateCar successfully updated Id {Id}.", id);
        return new OkObjectResult(incoming);
    }

    [Function("ValidateCars")]
    public async Task<IActionResult> ValidateCars(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "cars/validate")] HttpRequest req,
        FunctionContext context)
    {
        var logger = context.GetLogger("ValidateCars");
        logger.LogInformation("ValidationTriggered for Cars.");

        if (!Authorized(req))
        {
            logger.LogWarning("Unauthorized request to PATCH /cars/validate.");
            return new UnauthorizedResult();
        }

        var currentYear = DateTime.UtcNow.Year;
        var thresholdYear = currentYear - 20;
        logger.LogInformation("ValidateCars using threshold year {ThresholdYear}.", thresholdYear);

        var connString = GetSqlConnectionString();
        int updatedCount;

        try
        {
            using (var conn = new SqlConnection(connString))
            {
                await conn.OpenAsync();
                logger.LogInformation("SQL connection opened for ValidateCars.");

                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Cars
                    SET IsClassic = 1
                    WHERE
                        TRY_CONVERT(int, [Year]) < @ThresholdYear
                        AND (IsClassic = 0 OR IsClassic IS NULL);";

                cmd.Parameters.AddWithValue("@ThresholdYear", thresholdYear);

                updatedCount = await cmd.ExecuteNonQueryAsync();
                logger.LogInformation("ValidateCars updated {Count} rows to IsClassic = 1.", updatedCount);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ValidateCars database error.");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        var result = new
        {
            updatedCount,
            timestamp = DateTime.UtcNow.ToString("o")
        };

        logger.LogInformation("ValidateCars completed at {Timestamp} with {Count} updates.",
            result.timestamp, updatedCount);

        return new OkObjectResult(result);
    }

    // DELETE: remove a car by Id
    [Function("DeleteCar")]
    public async Task<IActionResult> DeleteCar(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cars/{id:guid}")] HttpRequest req,
        Guid id,
        FunctionContext context)
    {
        var logger = context.GetLogger("DeleteCar");
        logger.LogInformation("DeleteCar triggered for Id {Id}.", id);

        if (!Authorized(req))
        {
            logger.LogWarning("Unauthorized request to DELETE /cars/{Id}.", id);
            return new UnauthorizedResult();
        }

        var connString = GetSqlConnectionString();
        int rowsAffected;

        try
        {
            using (var conn = new SqlConnection(connString))
            {
                await conn.OpenAsync();
                logger.LogInformation("SQL connection opened for DeleteCar.");

                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Cars WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", id);

                rowsAffected = await cmd.ExecuteNonQueryAsync();
                logger.LogInformation("DeleteCar DELETE executed. Rows affected: {Rows}", rowsAffected);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DeleteCar database error for Id {Id}.", id);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        if (rowsAffected == 0)
        {
            logger.LogWarning("DeleteCar found no record with Id {Id}.", id);
            return new NotFoundObjectResult(new { error = "Not found" });
        }

        logger.LogInformation("DeleteCar successfully deleted Id {Id}.", id);
        return new OkObjectResult(new { message = "Deleted" });
    }
}

