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

### 2. Use the Middleware

The `CqApiAttributeMiddleware` must be added **BEFORE** `UseRouting()` to apply the custom attributes to endpoints:

```csharp
app.UseRouting();
app.UseCqApiAttributeMiddleware(); // Add this!
app.UseRateLimiter(); // Now rate limiting can see the attributes
app.UseAuthentication();
app.UseAuthorization();
```

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
4. **Middleware**: `CqApiAttributeMiddleware` intercepts requests, resolves the handler, and dynamically applies custom attributes to the endpoint metadata
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

## Breaking Changes

None! This is a purely additive feature. Existing CqApi applications continue to work without any changes.

## Performance

The middleware adds minimal overhead:

- Handler lookup is O(1) dictionary lookup
- Attribute application only occurs when custom attributes are configured
- No impact if `PreserveCustomAttributes` is empty

## Notes

- The middleware must be placed **before** `UseRouting()` to work correctly
- Attributes are applied per-request, allowing dynamic behavior
- The middleware only processes requests matching the CqApi URL prefix
