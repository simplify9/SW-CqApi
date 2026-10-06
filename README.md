# SW.CqApi

![Build Status](https://github.com/simplify9/SW-CqApi/actions/workflows/nuget-publish.yml/badge.svg)
[![NuGet Version](https://img.shields.io/nuget/v/SimplyWorks.CqApi?style=for-the-badge)](https://www.nuget.org/packages/SimplyWorks.CqApi/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)

**SW.CqApi** is a powerful .NET library that eliminates boilerplate code by automatically routing HTTP requests to handler classes based on folder structure and namespaces. It follows Command Query Responsibility Segregation (CQRS) principles and convention-over-configuration design.

## 🚀 Key Features

- **Zero Boilerplate Controllers**: Automatically generates API routes based on folder structure
- **CQRS Pattern Support**: Built-in interfaces for commands, queries, and specialized handlers
- **Convention-Based Routing**: Uses namespaces to determine API endpoints
- **OpenAPI Integration**: Automatic Swagger documentation generation
- **Authentication & Authorization**: Built-in JWT and role-based security
- **Type-Safe Serialization**: Configurable JSON serialization with proper type mapping
- **Validation Support**: FluentValidation integration out of the box
- **One Endpoint per Handler** (v10): real ASP.NET Core endpoints, so routing, rate limiting, authorization and tracing see each handler

> **Upgrading from 8.x?** See [MIGRATION-10.md](MIGRATION-10.md). CqApi 10 targets .NET 10 and needs one extra line: `endpoints.MapCqApi()`.

## 📦 Installation

Install via NuGet Package Manager:

```bash
dotnet add package SimplyWorks.CqApi
```

Or via Package Manager Console:

```powershell
Install-Package SimplyWorks.CqApi
```

## 🏗️ Project Structure

SW.CqApi uses a convention-based approach where your folder structure determines your API routes:

```
📁 Resources/
├── 📁 Users/
│   ├── Create.cs          → POST /api/users
│   ├── GetById.cs         → GET /api/users/{id}
│   ├── Search.cs          → GET /api/users
│   └── Delete.cs          → DELETE /api/users/{id}
├── 📁 Orders/
│   ├── Create.cs          → POST /api/orders  
│   ├── GetById.cs         → GET /api/orders/{id}
│   └── UpdateStatus.cs    → POST /api/orders/{id}/updatestatus
└── 📁 Reports/
    └── Generate.cs        → GET /api/reports
```

## 🔧 Quick Start

### 1. Configure Services

In your `Startup.cs` or `Program.cs`:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddCqApi(config =>
    {
        config.ApplicationName = "My API";
        config.Description = "API built with SW.CqApi";
        config.UrlPrefix = "api";  // Default route prefix
        config.ProtectAll = false; // Set to true for default authentication
    });
    
    // Add your other services...
}

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    app.UseRouting();
    app.UseAuthorization();

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
        endpoints.MapCqApi();   // one endpoint per handler
    });
}
```

### 2. Create Your First Handler

Create a handler class in the `Resources` folder:

```csharp
// Resources/Users/Create.cs
using SW.PrimitiveTypes;

namespace MyApp.Resources.Users
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }

    [Returns(StatusCode = 201, Type = typeof(int), Description = "User created successfully")]
    public class Create : ICommandHandler<CreateUserRequest, int>
    {
        public async Task<int> Handle(CreateUserRequest request)
        {
            // Your business logic here
            var userId = await CreateUser(request);
            return userId;
        }
        
        private async Task<int> CreateUser(CreateUserRequest request)
        {
            // Implementation
            return new Random().Next(1000, 9999);
        }
    }
}
```

This automatically creates a `POST /api/users` endpoint that accepts a JSON body and returns an integer.

## 📋 Handler Interfaces

SW.CqApi provides several interfaces for different HTTP operations:

### Command Handlers (POST/PUT operations)

| Interface | Route | Description |
|-----------|-------|-------------|
| `ICommandHandler` | `POST /resource` | Command with no parameters or return value |
| `ICommandHandler<TRequest>` | `POST /resource` | Command with request body |
| `ICommandHandler<TRequest, TResponse>` | `POST /resource` | Command with request body and response |
| `ICommandHandler<TKey, TRequest, TResponse>` | `POST /resource/{key}` | Command with key parameter and request body |

### Query Handlers (GET operations)

| Interface | Route | Description |
|-----------|-------|-------------|
| `IQueryHandler` | `GET /resource` | Simple query with no parameters |
| `IQueryHandler<TRequest>` | `GET /resource?params` | Query with query string parameters |
| `IQueryHandler<TKey, TRequest>` | `GET /resource/{key}?params` | Query with key and query parameters |

### Specialized Handlers

| Interface | Route | Description |
|-----------|-------|-------------|
| `IGetHandler<TKey, TResponse>` | `GET /resource/{key}` | Get single item by key |
| `IDeleteHandler<TKey>` | `DELETE /resource/{key}` | Delete item by key |
| `ISearchyHandler` | `GET /resource` | Advanced search with sorting and filtering |

## 🔍 Advanced Examples

### Query Handler with Parameters

```csharp
// Resources/Users/Search.cs
public class SearchRequest
{
    public string Name { get; set; }
    public int? Age { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;
}

public class SearchResult
{
    public List<UserDto> Users { get; set; }
    public int TotalCount { get; set; }
}

public class Search : IQueryHandler<SearchRequest, SearchResult>
{
    public async Task<SearchResult> Handle(SearchRequest request)
    {
        // Query logic here
        return new SearchResult 
        { 
            Users = await GetUsers(request),
            TotalCount = await GetUsersCount(request)
        };
    }
}
```

**Usage**: `GET /api/users?name=john&age=25&skip=0&take=10`

### Get Handler with Key

```csharp
// Resources/Users/GetById.cs
public class GetById : IGetHandler<int, UserDto>
{
    public async Task<UserDto> Handle(int userId)
    {
        return await GetUserById(userId);
    }
}
```

**Usage**: `GET /api/users/123`

### Command with Key and Body

```csharp
// Resources/Orders/UpdateStatus.cs
public class UpdateStatusRequest
{
    public OrderStatus Status { get; set; }
    public string Reason { get; set; }
}

public class UpdateStatus : ICommandHandler<int, UpdateStatusRequest, bool>
{
    public async Task<bool> Handle(int orderId, UpdateStatusRequest request)
    {
        return await UpdateOrderStatus(orderId, request.Status, request.Reason);
    }
}
```

**Usage**: `POST /api/orders/123/updatestatus`

## ⚙️ Configuration Options

Configure SW.CqApi with various options:

```csharp
services.AddCqApi(config =>
{
    // Basic Configuration
    config.ApplicationName = "My API";
    config.Description = "API Documentation";
    config.UrlPrefix = "api";
    
    // Security
    config.ProtectAll = true;  // Require authentication for all endpoints
    config.RolePrefix = "MyApp.";  // Prefix for role-based authorization
    
    // Custom resource descriptions
    config.ResourceDescriptions.Add("Users", "User management operations");
    config.ResourceDescriptions.Add("Orders", "Order processing operations");
    
    // Authentication configuration
    config.AuthOptions.ParameterLocation = ParameterLocation.Header;
    config.AuthOptions.ParameterName = "Authorization";
    
    // Custom type mappings
    config.Maps.Add<DateTime, string>(dt => dt.ToString("yyyy-MM-dd"));
    
    // Disable OpenAPI documentation
    config.DisableOpenApiDocumentation = false;
});
```

## 🔐 Authentication & Authorization

### JWT Authentication

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "your-issuer",
                ValidAudience = "your-audience",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("your-secret-key"))
            };
        });
        
    services.AddCqApi(config =>
    {
        config.ProtectAll = true;  // Protect all endpoints by default
    });
}
```

### Role-Based Authorization

Use the `[Authorize]` attribute on handlers:

```csharp
[Authorize(Roles = "Admin")]
public class Delete : IDeleteHandler<int>
{
    public async Task Handle(int userId)
    {
        await DeleteUser(userId);
    }
}
```

Or use the `[UnProtect]` attribute to allow anonymous access when `ProtectAll = true`:

```csharp
[UnProtect]
public class PublicInfo : IQueryHandler<PublicInfoDto>
{
    public async Task<PublicInfoDto> Handle()
    {
        return await GetPublicInformation();
    }
}
```

## 📊 OpenAPI/Swagger Integration

SW.CqApi automatically generates OpenAPI documentation. Access it at:

- Swagger JSON: `GET /api/swagger.json` (served as `application/json`; behind `UsePathBase`, the path base is declared as the document's server so Swagger UI's "Try it out" keeps it)
- Built-in Swagger UI: Configure in your startup

Use the `[Returns]` attribute to document response types:

```csharp
[Returns(StatusCode = 200, Type = typeof(List<UserDto>), Description = "List of users")]
[Returns(StatusCode = 404, Description = "No users found")]
public class GetAll : IQueryHandler<List<UserDto>>
{
    // Implementation
}
```

## 🧪 Testing

SW.CqApi handlers are easy to test since they're just classes with dependencies:

```csharp
[TestMethod]
public async Task CreateUser_ShouldReturnUserId()
{
    // Arrange
    var handler = new Create(mockUserService, mockLogger);
    var request = new CreateUserRequest 
    { 
        Name = "John Doe", 
        Email = "john@example.com" 
    };

    // Act
    var result = await handler.Handle(request);

    // Assert
    Assert.IsTrue(result > 0);
}
```

## 🔧 Advanced Features

### Custom Serialization

```csharp
services.AddCqApi(config =>
{
    config.Serializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
    config.Serializer.DateFormatHandling = DateFormatHandling.IsoDateFormat;
});
```

### Validation Integration

SW.CqApi works seamlessly with FluentValidation:

```csharp
public class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

// Register in DI
services.AddTransient<IValidator<CreateUserRequest>, CreateUserValidator>();
```

### Background Processing

For long-running operations, return status codes appropriately:

```csharp
[Returns(StatusCode = 202, Description = "Processing started")]
public class ProcessLargeFile : ICommandHandler<ProcessFileRequest>
{
    public async Task Handle(ProcessFileRequest request)
    {
        // Start background processing
        await StartBackgroundJob(request);
    }
}
```

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details.

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support & Issues

- **Issues**: [GitHub Issues](https://github.com/simplify9/SW-CqApi/issues)
- **Discussions**: [GitHub Discussions](https://github.com/simplify9/SW-CqApi/discussions)
- **Documentation**: [Wiki](https://github.com/simplify9/SW-CqApi/wiki)

If you encounter any bugs or have feature requests, please don't hesitate to [create an issue](https://github.com/simplify9/SW-CqApi/issues/new). We'll get back to you promptly!

---

Made with ❤️ by [Simplify9](https://github.com/simplify9) 
 
