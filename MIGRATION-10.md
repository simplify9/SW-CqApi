# Migrating to CqApi 10

CqApi 10 maps **one ASP.NET Core endpoint per handler** instead of routing every request
through a single controller. URLs, status codes and response bodies are the same as 8.2.x; what
changes is how the app wires CqApi in, and that routing, middleware and tracing can now see which
handler a request is for.

## What you change

1. **Target .NET 10** in the web project:

   ```xml
   <TargetFramework>net10.0</TargetFramework>
   ```

   Only the web project has to move. Class libraries and SDK packages on `net8.0` or
   `netstandard2.0` keep working, and so do EF Core 8 / Npgsql 8 (verified on Traxis Accounting).

2. **Reference the package:**

   ```xml
   <PackageReference Include="SimplyWorks.CqApi" Version="10.0.*" />
   ```

3. **Map the endpoints** next to `MapControllers()`:

   ```csharp
   app.UseEndpoints(endpoints =>
   {
       endpoints.MapControllers();
       endpoints.MapCqApi();   // new
   });

   // or, with minimal hosting
   app.MapControllers();
   app.MapCqApi();
   ```

   `MapCqApi()` lives in `Microsoft.AspNetCore.Builder`, like `MapControllers()`, so no extra
   `using` is needed. Forget it and every CqApi URL answers 404; CqApi logs a critical message at
   startup when handlers are registered but no CqApi endpoints are mapped.

4. **Remove** `app.UseCqApiAttributeMiddleware()` if you call it. It is now a no-op marked
   obsolete: attributes listed in `PreserveCustomAttributes` are endpoint metadata from startup.

That is the whole migration for a typical service. `AddCqApi(...)` and its options, handlers,
validators, `[Protect]`/`[Unprotect]`, `[HandlerName]`, `[Returns]` and the Newtonsoft
serializer settings are unchanged.

## What stays the same

Checked against 8.2.15 with the same handlers, request by request (status, headers, body bytes):

- **URLs**, including precedence: a named handler (`GET shipments/search`) still wins over a key
  (`GET shipments/{key}`), now through ordinary route precedence.
- **Response bodies and content types.** Results are still MVC action results executed through
  MVC's formatters, so primitives, strings, `206` lookups, `ICqApiResult` (202/301/content/bytes),
  `204` for `null` and the Newtonsoft-written JSON are byte-identical.
- **Errors.** `SWNotFoundException` → 404 with the message; `SWValidationException`, other
  `SWException`s and FluentValidation failures → 400 with the same `{"Field.X": [...]}` shape;
  `SWUnauthorizedException`/`SWForbiddenException` → 401; bare 404/401 → `ProblemDetails`; a bad
  `lookup` value → the same validation `ProblemDetails`. Exceptions are logged under the same
  category as before (`SW.CqApi.CqApiExceptionFilterAttribute`), so log queries keep matching.
- **Unknown routes** get the same 404 bodies as before (e.g. `nothing/get` as text for an unknown
  resource).
- **Authentication**: endpoints carry the old controller's
  `[Authorize(AuthenticationSchemes = "Bearer")]` + `[AllowAnonymous]`, so the Bearer scheme still
  fills `HttpContext.User` and CqApi's own protection decides what's refused.

On Traxis Accounting, 30 authenticated requests against the staging database (≈620 KB of
responses, including the error paths) were identical to the deployed 8.2.15 service.

## What's different

- **An unhandled handler exception surfaces as itself**, not wrapped in
  `TargetInvocationException`. The client still gets a 500; logs and the developer exception page
  now show the real exception and stack.
- **`swagger.json` is served as `application/json`** (it was `text/plain`).
- **Requests are cheaper.** Handlers are called through delegates compiled at startup, and
  protection, required roles and validator types are worked out once rather than by reflection on
  every request. Handlers are no longer constructed at startup just to discover them.
- **Removed**: `CqApiController` (an obsolete, non-controller type of that name remains so
  `AddApplicationPart(typeof(CqApiController).Assembly)` in test hosts still compiles; you can drop
  that call), the `cqapiPrefix` route constraint, `UseCqApi()` (the version/locale path rewriter),
  and the `Newtonsoft.Json.Schema` dependency.

## New, opt-in

- **`CqApiHandlerMetadata` on every endpoint** — resource, handler key and handler type — plus a
  real route pattern and display name (`POST api/shipments/{key}/saveitem`). Tracing and APM name
  requests after the route instead of nine generic controller actions:

  ```csharp
  var handler = context.GetEndpoint()?.Metadata.GetMetadata<CqApiHandlerMetadata>();
  ```

- **`ReturnForbiddenAs403`** (default `false`): answer `SWForbiddenException` and a missing
  required role with 403 instead of 401. Turn it on only once your clients handle 403; many treat
  401 as "sign in again".

  ```csharp
  services.AddCqApi(o => o.ReturnForbiddenAs403 = true);
  ```

- **Standard endpoint conventions** apply to all CqApi endpoints at once:

  ```csharp
  app.MapCqApi().RequireRateLimiting("default");
  ```

## Test hosts

Replace

```csharp
services.AddControllers().AddApplicationPart(typeof(CqApiController).Assembly);
...
endpoints.MapControllers();
```

with

```csharp
services.AddControllers();
...
endpoints.MapControllers();
endpoints.MapCqApi();
```

## Trying a prerelease

Running the publish workflow manually (`workflow_dispatch`) from a branch pushes a SemVer
prerelease such as `10.0.3-my-branch.12`, which stable version ranges never pick up. Reference it
explicitly to trial a branch build in a real deployment.
