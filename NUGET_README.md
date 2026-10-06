# SW.CqApi - Convention-Based REST API Framework

[![NuGet Version](https://img.shields.io/nuget/v/SimplyWorks.CqApi)](https://www.nuget.org/packages/SimplyWorks.CqApi/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

**SW.CqApi** eliminates boilerplate code by automatically routing HTTP requests to handler classes based on folder structure and namespaces. Build REST APIs with zero controllers using CQRS patterns and convention-over-configuration.

## Quick Start

```bash
dotnet add package SimplyWorks.CqApi
```

```csharp
// Program.cs
builder.Services.AddCqApi(config =>
{
    config.ApplicationName = "My API";
    config.UrlPrefix = "api";
});
...
app.MapCqApi();   // one endpoint per handler (CqApi 10)

// Resources/Users/Create.cs  → POST /api/users
public class Create : ICommandHandler<CreateUserRequest, int>
{
    public async Task<int> Handle(CreateUserRequest request)
    {
        // Your business logic here
        return userId;
    }
}
```

## Key Features

- 🚀 **Zero Boilerplate**: No controllers needed - routes generated from folder structure
- 📋 **CQRS Support**: Built-in command/query interfaces
- 🔐 **Authentication**: JWT and role-based security
- 📖 **Auto Documentation**: OpenAPI/Swagger generation
- ✅ **Validation**: FluentValidation integration

Upgrading from 8.x: see [MIGRATION-10.md](https://github.com/simplify9/SW-CqApi/blob/main/MIGRATION-10.md).

## Full Documentation

For complete documentation, examples, and advanced features, visit:

**📚 [GitHub Repository & Documentation](https://github.com/simplify9/SW-CqApi)**

## Support

- 🐛 [Issues](https://github.com/simplify9/SW-CqApi/issues)
- 💬 [Discussions](https://github.com/simplify9/SW-CqApi/discussions)

---
Made with ❤️ by [Simplify9](https://github.com/simplify9)