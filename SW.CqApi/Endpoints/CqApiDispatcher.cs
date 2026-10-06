using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SW.CqApi.Serialization;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.CqApi.Endpoints
{
    /// <summary>
    /// Runs one handler for one request. This is the body of the old CqApiController, kept
    /// result for result: handlers still produce MVC action results, executed through MVC's own
    /// executors and formatters, so status codes, bodies and content types don't change.
    /// </summary>
    internal static class CqApiDispatcher
    {
        // Same category as the old exception filter, so existing log queries keep matching.
        private const string ExceptionLogCategory = "SW.CqApi.CqApiExceptionFilterAttribute";

        private static readonly ConcurrentDictionary<Type, Type> validatorTypes = new();

        public static async Task Execute(HttpContext httpContext, HandlerInfo handlerInfo, string key, bool acceptsLookup)
        {
            var services = httpContext.RequestServices;
            var actionContext = new ActionContext(httpContext, httpContext.GetRouteData(), new ActionDescriptor());

            IActionResult result;
            try
            {
                var lookup = false;
                IActionResult invalid = null;
                if (acceptsLookup && !TryReadLookup(httpContext, actionContext, out lookup, out invalid))
                    result = invalid;
                else
                    result = await ExecuteHandler(httpContext, actionContext, handlerInfo, key, lookup);
            }
            catch (Exception ex)
            {
                result = MapException(services, actionContext, ex);
                if (result == null) throw;
            }

            await ExecuteResult(services, actionContext, result);
        }

        /// <summary>Old route for a resource or handler that doesn't exist: same 404 bodies as before.</summary>
        public static async Task NotFound(HttpContext httpContext, string handlerKey)
        {
            var services = httpContext.RequestServices;
            var actionContext = new ActionContext(httpContext, httpContext.GetRouteData(), new ActionDescriptor());

            IActionResult result = handlerKey == null
                ? new NotFoundResult()
                : MapException(services, actionContext,
                    new SWNotFoundException($"{httpContext.GetRouteValue("resourceName")}/{handlerKey}"));

            await ExecuteResult(services, actionContext, result);
        }

        public static Task ExecuteResult(IServiceProvider services, ActionContext actionContext, IActionResult result)
        {
            // What [ApiController]'s ClientErrorResultFilter did: bare 4xx results become ProblemDetails.
            if (result is IClientErrorActionResult { StatusCode: >= 400 } clientError &&
                !services.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value.SuppressMapClientErrors)
            {
                result = services.GetRequiredService<IClientErrorFactory>().GetClientError(actionContext, clientError) ?? result;
            }

            return result.ExecuteResultAsync(actionContext);
        }

        private static async Task<IActionResult> ExecuteHandler(HttpContext httpContext, ActionContext actionContext, HandlerInfo handlerInfo, string key, bool lookup)
        {
            var services = httpContext.RequestServices;
            var request = httpContext.Request;
            var options = services.GetRequiredService<CqApiOptions>();
            var handler = services.GetHandlerInstance(handlerInfo);
            var invoker = handlerInfo.Invoker;
            var type = handlerInfo.NormalizedInterfaceType;

            if (type == typeof(ISearchyHandler))
            {
                var searchyRequest = new SearchyRequest(request.QueryString.Value);
                var result = await invoker.Invoke(handler, searchyRequest, lookup, null);
                return lookup ? new ObjectResult(result) { StatusCode = 206 } : SendOkResult(result, options);
            }

            if (type == typeof(IQueryHandler<>) || type == typeof(ICommandHandler<>))
                return HandleResult(httpContext, await invoker.Invoke(handler), options);

            if (type == typeof(IQueryHandler<,>))
            {
                var query = request.Query.GetInstance(handlerInfo.ArgumentTypes[0]);
                return HandleResult(httpContext, await invoker.Invoke(handler, query), options);
            }

            if (type == typeof(IQueryHandler<,,>))
            {
                var keyParam = ConvertKey(key, handlerInfo.ArgumentTypes[0]);
                var query = request.Query.GetInstance(handlerInfo.ArgumentTypes[1]);
                return HandleResult(httpContext, await invoker.Invoke(handler, keyParam, query), options);
            }

            if (type == typeof(ICommandHandler<,>))
            {
                var body = await ReadBody(request, options.Serializer, handlerInfo.ArgumentTypes[0]);
                var invalid = await ValidateInput(services, actionContext, body);
                if (invalid != null) return invalid;
                return HandleResult(httpContext, await invoker.Invoke(handler, body), options);
            }

            if (type == typeof(ICommandHandler<,,>))
            {
                var keyParam = ConvertKey(key, handlerInfo.ArgumentTypes[0]);
                var body = await ReadBody(request, options.Serializer, handlerInfo.ArgumentTypes[1]);
                var invalid = await ValidateInput(services, actionContext, body);
                if (invalid != null) return invalid;
                return HandleResult(httpContext, await invoker.Invoke(handler, keyParam, body), options);
            }

            if (type == typeof(IGetHandler<,>))
            {
                var keyParam = ConvertKey(key, handlerInfo.ArgumentTypes[0]);
                var result = await invoker.Invoke(handler, keyParam);
                if (result == null) return new NotFoundResult();
                return lookup ? new ObjectResult(result) { StatusCode = 206 } : HandleResult(httpContext, result, options);
            }

            if (type == typeof(IDeleteHandler<,>))
            {
                var keyParam = ConvertKey(key, handlerInfo.ArgumentTypes[0]);
                await invoker.Invoke(handler, keyParam);
                return new AcceptedResult();
            }

            return new NotFoundResult();
        }

        private static object ConvertKey(string key, Type type)
        {
            try
            {
                return key.ConvertValueToType(type);
            }
            catch (Exception ex)
            {
                throw new BadInputFormatException(ex);
            }
        }

        /// <summary>
        /// Reads the request body straight from the stream into the handler's request type.
        /// An empty or <c>null</c> body is refused.
        /// </summary>
        private static async Task<object> ReadBody(HttpRequest request, JsonSerializer serializer, Type type)
        {
            object body;
            try
            {
                body = await request.ReadJsonAsync(serializer, type);
            }
            // Any failure to turn the body into the request type is bad input. I/O failures
            // (client abort, body over the size limit) keep their own handling.
            catch (Exception ex) when (ex is not IOException and not OperationCanceledException)
            {
                throw new BadInputFormatException(ex);
            }

            if (body is null)
                throw new BadInputFormatException(new JsonSerializationException("A non-empty request body is required."));

            return body;
        }

        private static async Task<IActionResult> ValidateInput(IServiceProvider services, ActionContext actionContext, object input)
        {
            var validatorType = validatorTypes.GetOrAdd(input.GetType(), t => typeof(IValidator<>).MakeGenericType(t));

            if (services.GetService(validatorType) is not IValidator validator) return null;

            var validationResult = await validator.ValidateAsync(new ValidationContext<object>(input));

            if (validationResult.IsValid) return null;

            foreach (var error in validationResult.Errors)
                actionContext.ModelState.AddModelError($"Field.{error.PropertyName}", error.ErrorMessage);

            return new BadRequestObjectResult(actionContext.ModelState);
        }

        /// <summary>The old <c>[FromQuery(Name = "lookup")] bool lookup</c> binding, including its 400 for a bad value.</summary>
        private static bool TryReadLookup(HttpContext httpContext, ActionContext actionContext, out bool lookup, out IActionResult invalid)
        {
            lookup = false;
            invalid = null;

            var values = httpContext.Request.Query["lookup"];
            if (values.Count == 0) return true;
            if (bool.TryParse(values[0], out lookup)) return true;

            // MVC's two messages: one for an empty value, one for an unparsable value.
            actionContext.ModelState.AddModelError("lookup", string.IsNullOrEmpty(values[0])
                ? "The value '' is invalid."
                : $"The value '{values[0]}' is not valid.");
            invalid = httpContext.RequestServices.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value
                .InvalidModelStateResponseFactory(actionContext);
            return false;
        }

        private static IActionResult SendOkResult(object result, CqApiOptions options)
        {
            if (result.GetType().IsPrimitive || result is decimal or string)
                return new OkObjectResult(result);

            return new NewtonsoftJsonResult(result, options.Serializer);
        }

        private static IActionResult HandleResult(HttpContext httpContext, object result, CqApiOptions options)
        {
            switch (result)
            {
                case ICqApiResult cqApiResult:
                {
                    foreach (var kvp in cqApiResult.Headers)
                        httpContext.Response.Headers.Append(kvp.Key, kvp.Value);

                    switch (cqApiResult.Status)
                    {
                        case CqApiResultStatus.UnderProcessing:
                            // ControllerBase.Accepted(string) sets Location and sends no body.
                            return new AcceptedResult(cqApiResult.Result.ToString(), null);
                        case CqApiResultStatus.ChangedLocation:
                            return new RedirectResult(cqApiResult.Result.ToString() ?? string.Empty, permanent: true);
                        default:
                            switch (cqApiResult.Result)
                            {
                                case null:
                                    return new NoContentResult();
                                case string stringResult:
                                    return new ContentResult
                                    {
                                        StatusCode = cqApiResult.Status == CqApiResultStatus.Ok ? 200 : 400,
                                        Content = stringResult,
                                        ContentType = cqApiResult.ContentType,
                                    };
                                case byte[] bytes:
                                    return new FileContentResult(bytes, cqApiResult.ContentType);
                            }
                            break;
                    }
                    break;
                }
                case null:
                    return new NoContentResult();
            }

            return SendOkResult(result, options);
        }

        /// <summary>
        /// What the old exception filter did. Returns null for an exception it doesn't handle,
        /// which is logged as an error and rethrown to the host (developer page or a bare 500).
        /// </summary>
        private static IActionResult MapException(IServiceProvider services, ActionContext actionContext, Exception exception)
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(ExceptionLogCategory);

            if (exception is TargetInvocationException { InnerException: SWException } invocation)
                exception = invocation.InnerException;

            if (exception is not SWException)
            {
                logger.LogError(exception, string.Empty);
                return null;
            }

            IActionResult result;
            if (exception is SWNotFoundException)
                result = new NotFoundObjectResult(exception.Message);

            else if (exception is SWForbiddenException && services.GetRequiredService<CqApiOptions>().ReturnForbiddenAs403)
                result = new StatusCodeResult(StatusCodes.Status403Forbidden);

            else if (exception is SWForbiddenException or SWUnauthorizedException)
                result = new UnauthorizedResult();

            else if (exception is SWValidationException validationException)
            {
                foreach (var kvp in validationException.Validations)
                    actionContext.ModelState.AddModelError(kvp.Key, kvp.Value);

                result = new BadRequestObjectResult(actionContext.ModelState);
            }
            else
            {
                actionContext.ModelState.AddModelError(exception.GetType().Name, exception.Message);
                result = new BadRequestObjectResult(actionContext.ModelState);
            }

            logger.LogWarning(exception, string.Empty);
            return result;
        }
    }
}
