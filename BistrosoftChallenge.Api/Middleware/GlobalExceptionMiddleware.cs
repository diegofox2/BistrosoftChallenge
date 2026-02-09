using System.Net;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace BistrosoftChallenge.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred.");
                
                // Fire and forget logging so we don't delay the response too much, 
                // or await it if reliability is critical. For exception handling, usually safe to await briefly.
                await SendLogToSolarWindsAsync(ex, context);
                
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task SendLogToSolarWindsAsync(Exception ex, HttpContext context)
        {
            try
            {
                var logUrl = _configuration["SolarWinds:Url"];
                var logToken = _configuration["SolarWinds:Token"];

                if (string.IsNullOrEmpty(logUrl) || string.IsNullOrEmpty(logToken))
                {
                    _logger.LogWarning("SolarWinds logging skipped: Url or Token not configured.");
                    return;
                }

                var client = _httpClientFactory.CreateClient();
                
                // Construct a log entry. 
                // SolarWinds Observability / Papertrail generally parses JSON bodies.
                var logEntry = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    severity = "Error",
                    message = ex.Message,
                    details = ex.StackTrace,
                    requestPath = context.Request.Path.ToString(),
                    method = context.Request.Method
                };

                var json = JsonSerializer.Serialize(logEntry);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Authenticate using Basic Auth with the Token as the username
                var authBytes = Encoding.ASCII.GetBytes($"{logToken}:");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                var response = await client.PostAsync(logUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"Failed to send log to SolarWinds. Status: {response.StatusCode}. Response: {responseBody}");
                }
            }
            catch (Exception logEx)
            {
                // Prevent logging failure from crashing the request if it wasn't already crashed
                _logger.LogWarning(logEx, "Exception while attempting to send log to SolarWinds.");
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                StatusCode = context.Response.StatusCode,
                Message = "Internal Server Error",
                Detailed = exception.Message // Removing in production recommended, but acceptable for challenge
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
