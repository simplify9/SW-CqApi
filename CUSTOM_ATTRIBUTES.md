# Custom Attributes Support in CqApi

## Overview

CqApi now supports preserving and applying custom ASP.NET Core attributes from handler classes to endpoints. This allows CqApi handlers to work seamlessly with ASP.NET Core middleware that relies on endpoint metadata, such as rate limiting, CORS policies, authorization policies, etc.

## The Problem

Previously, CqApi used its own routing mechanism and only recognized specific attributes like `[Protect]`, `[Unprotect]`, and `[HandlerName]`. Standard ASP.NET Core attributes like `[EnableRateLimiting]` were ignored because CqApi's single-controller architecture bypassed the normal endpoint metadata system.

## The Solution

### 1. Configuration in `AddCqApi()`

You can now specify which custom attributes should be preserved and applied to endpoints:

```csharp
services.AddCqApi(options =>
{
    options.UrlPrefix = "api";

    // Tell CqApi to preserve these custom attributes
    options.PreserveCustomAttributes.Add(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute));
    options.PreserveCustomAttributes.Add(typeof(Microsoft.AspNetCore.Cors.EnableCorsAttribute));
    // Add any other attributes you need
});
```

### 2. Map the endpoints

Since CqApi 10 each handler is its own endpoint, and the preserved attributes are part of that
endpoint's metadata from startup. No extra middleware is needed; middleware such as the rate
limiter only has to run after `UseRouting()`, as it would for any endpoint:

```csharp
app.UseRouting();
app.UseRateLimiter(); // sees [EnableRateLimiting] on the handler's endpoint
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapCqApi();
```

(`UseCqApiAttributeMiddleware()` from 8.x is now an obsolete no-op; remove the call.)

### 3. Apply Attributes to Handlers

Now you can use standard ASP.NET Core attributes on your handler classes:

```csharp
[Unprotect]
[HandlerName(nameof(UserExists))]
[EnableRateLimiting("ExistsChecksPolicy")] // This now works!
public class UserExists : IGetHandler<string, object>
{
    public async Task<object> Handle(string key)
    {
        // Your logic here
    }
}
```

## How It Works

1. **Configuration**: `PreserveCustomAttributes` list tells CqApi which attribute types to capture
2. **Discovery**: During handler discovery, `ServiceDiscovery` captures specified custom attributes from each handler class
3. **Storage**: Custom attributes are stored in `HandlerInfo.CustomAttributes`
4. **Endpoints**: `MapCqApi()` adds them as metadata on the handler's own endpoint at startup
5. **ASP.NET Core**: Downstream middleware (rate limiting, CORS, etc.) can now see these attributes

## Example: Rate Limiting

### Configure Rate Limiting Policy

```csharp
services.AddRateLimiter(options =>
{
    options.AddPolicy("ExistsChecksPolicy", httpContext =>
    {
        var clientIp = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                       ?? httpContext.Connection.RemoteIpAddress?.ToString()
                       ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 70,
            Window = TimeSpan.FromMinutes(1)
        });
    });
});
```

### Apply to Handler

```csharp
[EnableRateLimiting("ExistsChecksPolicy")]
public class UserExists : IGetHandler<string, object>
{
    // Implementation
}
```

## Supported Attributes

You can preserve any attribute type by adding it to `PreserveCustomAttributes`. Common examples:

- `[EnableRateLimiting(policyName)]` - Rate limiting
- `[EnableCors(policyName)]` - CORS policies
- `[Authorize(policy)]` - Authorization policies (in addition to `[Protect]`)
- `[ResponseCache]` - Response caching
- Custom attributes you define

## Performance

Nothing happens per request: the attributes are read once at startup and stored as endpoint
metadata. In 8.x a middleware re-parsed every request's path to find the handler and rebuilt the
endpoint with the extra metadata.

## Notes

- Only attribute types listed in `PreserveCustomAttributes` are added, so an `[Authorize]` that
  sits on a handler but isn't listed doesn't start being enforced.
- `app.MapCqApi()` returns a convention builder, so a convention can also be applied to every
  CqApi endpoint at once, e.g. `app.MapCqApi().RequireRateLimiting("default")`.
