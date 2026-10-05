using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using SW.CqApi.Extensions;
using SW.CqApi.Serialization;
using SW.HttpExtensions;

namespace SW.CqApi
{
    [CqApiExceptionFilter]
    [Route("{prefix:cqapiPrefix}")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer")]
    [AllowAnonymous]
    public class CqApiController : ControllerBase
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly ILogger<CqApiController> _logger;
        private readonly CqApiOptions _options;

        public CqApiController(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = serviceProvider.GetService<ILogger<CqApiController>>();
            _serviceDiscovery = serviceProvider.GetService<ServiceDiscovery>();
            _options = this._serviceProvider.GetService<CqApiOptions>();
        }

        [HttpGet("_roles")]
        public ActionResult<IDictionary<string, string>> GetRoles()
        {
            var sd = _serviceProvider.GetService<ServiceDiscovery>();
            return Ok(sd.GetRoles().OrderBy(e => e).ToDictionary(k => k, v => v));
        }

        [HttpGet("_roles/{role}")]
        public ActionResult<string> GetRole(string role)
        {
            return role;
        }

        [HttpGet("swagger.json")]
        public ActionResult<string> GetOpenApiDocument()
        {
            if (_options.DisableOpenApiDocumentation) return NotFound("OpenAPI documentation is disabled.");
            var sd = _serviceProvider.GetService<ServiceDiscovery>();
            return Ok(sd.GetOpenApiDocument());
        }

        [HttpGet("{resourceName}")]
        public Task<IActionResult> Get(
            string resourceName,
            [FromQuery(Name = "lookup")] bool lookup)
        {
            var handlerInfo = _serviceDiscovery.ResolveHandler(resourceName, "get");
            return ExecuteHandler(handlerInfo, null, lookup);
        }

        [HttpGet("{resourceName}/{key}/{token}")]
        public async Task<IActionResult> GetWithTokenAndKey(
            string resourceName,
            string token,
            string key,
            [FromQuery(Name = "lookup")] bool lookup)
        {
            var handler = _serviceDiscovery.ResolveHandler(resourceName, $"get/key/{token}");
            return await ExecuteHandler(handler, key, lookup);
        }

        [HttpGet("{resourceName}/{token}")]
        public async Task<IActionResult> GetWithToken(
            string resourceName,
            string token,
            [FromQuery(Name = "lookup")] bool lookup)
        {
            if (_serviceDiscovery.TryResolveHandler(resourceName, $"get/{token}", out var handlerInfo))
                return await ExecuteHandler(handlerInfo, null, lookup);

            if (_serviceDiscovery.TryResolveHandler(resourceName, "get/key", out handlerInfo))
                return await ExecuteHandler(handlerInfo, token, lookup);

            return NotFound();
        }

        [HttpPost("{resourceName}")]
        public async Task<IActionResult> Post(string resourceName)
        {
            var handlerInfo = _serviceDiscovery.ResolveHandler(resourceName, "post");
            return await ExecuteHandler(handlerInfo, null);
        }

        [HttpPost("{resourceName}/{token}")]
        public async Task<IActionResult> PostWithToken(string resourceName, string token)
        {
            if (_serviceDiscovery.TryResolveHandler(resourceName, $"post/{token}", out var handlerInfo))
                return await ExecuteHandler(handlerInfo, null);

            else if (_serviceDiscovery.TryResolveHandler(resourceName, "post/key", out handlerInfo))
                return await ExecuteHandler(handlerInfo, token);

            else
                return NotFound();
        }

        [HttpPost("{resourceName}/{key}/{command}")]
        public async Task<IActionResult> PostWithKeyAndCommandName(string resourceName, string key, string command)
        {
            var handlerInfo = _serviceDiscovery.ResolveHandler(resourceName, $"post/key/{command}");
            return await ExecuteHandler(handlerInfo, key);
        }

        [HttpDelete("{resourceName}/{key}")]
        public async Task<IActionResult> Delete(string resourceName, string key)
        {
            var handlerInfo = _serviceDiscovery.ResolveHandler(resourceName, "delete/key");
            return await ExecuteHandler(handlerInfo, key);
        }

        private async Task<bool> ValidateInput(object input)
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(input.GetType());

            if (_serviceProvider.GetService(validatorType) is not IValidator validator) return true;

            var context = new ValidationContext<object>(input);
            var validationResult = await validator.ValidateAsync(context);

            if (validationResult.IsValid) return true;

            foreach (var error in validationResult.Errors)
                ModelState.AddModelError($"Field.{error.PropertyName}", error.ErrorMessage);

            return false;
        }

        private IActionResult SendOkResult(object result)
        {
            if (result.GetType().IsPrimitive || result is decimal or string)
            {
                return Ok(result);
            }

            return new NewtonsoftJsonResult(result, _options.Serializer);
        }

        /// <summary>
        /// Reads the request body straight from the stream into the handler's request type.
        /// An empty or <c>null</c> body is refused, as it was when the body was bound by MVC first.
        /// </summary>
        private async Task<object> ReadBody(Type type)
        {
            object typedParam;
            try
            {
                typedParam = await Request.ReadJsonAsync(_options.Serializer, type);
            }
            // Any failure to turn the body into the request type is bad input, as before. I/O
            // failures (client abort, body over the size limit) keep their own handling.
            catch (Exception ex) when (ex is not IOException and not OperationCanceledException)
            {
                throw new BadInputFormatException(ex);
            }

            if (typedParam is null)
                throw new BadInputFormatException(new JsonSerializationException("A non-empty request body is required."));

            return typedParam;
        }

        private async Task<IActionResult> ExecuteHandler(HandlerInfo handlerInfo, string key, bool lookup = false)
        {
            var handlerInstance = await _serviceProvider.GetHandlerInstance(handlerInfo);

            if (handlerInfo.NormalizedInterfaceType == typeof(ISearchyHandler))
            {
                var searchyRequest = new SearchyRequest(Request.QueryString.Value);
                var result = await handlerInstance.Invoke(searchyRequest, lookup, null);
                return lookup ? StatusCode(206, result) : SendOkResult(result);
            }

            if (handlerInfo.NormalizedInterfaceType == typeof(IQueryHandler<>))
            {
                var result = await handlerInstance.Invoke();
                return HandleResult(result);
            }

            if (handlerInfo.NormalizedInterfaceType == typeof(IQueryHandler<,>))
            {
                var request = Request.Query.GetInstance(handlerInfo.ArgumentTypes[0]);
                var result = await handlerInstance.Invoke(request);
                return HandleResult(result);
            }

            if (handlerInfo.NormalizedInterfaceType == typeof(IQueryHandler<,,>))
            {
                object keyParam;
                try
                {
                    keyParam = key.ConvertValueToType(handlerInfo.ArgumentTypes[0]);
                }
                catch (Exception ex)
                {
                    throw new BadInputFormatException(ex);
                }

                var request = Request.Query.GetInstance(handlerInfo.ArgumentTypes[1]);
                var result = await handlerInstance.Invoke(keyParam, request);
                return HandleResult(result);
            }

            if (handlerInfo.NormalizedInterfaceType == typeof(ICommandHandler<>))
            {
                var result = await handlerInstance.Invoke();
                return HandleResult(result);
            }

            if (handlerInfo.NormalizedInterfaceType == typeof(ICommandHandler<,>))
            {
                var typedParam = await ReadBody(handlerInfo.ArgumentTypes[0]);

                if (!await ValidateInput(typedParam)) return BadRequest(ModelState);
                var result = await handlerInstance.Invoke(typedParam);
                return HandleResult(result);
            }

            if (handlerInfo.NormalizedInterfaceType == typeof(ICommandHandler<,,>))
            {
                object keyParam;
                try
                {
                    keyParam = key.ConvertValueToType(handlerInfo.ArgumentTypes[0]);
                }
                catch (Exception ex)
                {
                    throw new BadInputFormatException(ex);
                }

                var typedParam = await ReadBody(handlerInfo.ArgumentTypes[1]);

                if (!await ValidateInput(typedParam)) return BadRequest(ModelState);
                var result = await handlerInstance.Invoke(keyParam, typedParam);
                return HandleResult(result);
            }

            if (handlerInfo.NormalizedInterfaceType == typeof(IGetHandler<,>))
            {
                object keyParam;
                try
                {
                    keyParam = key.ConvertValueToType(handlerInfo.ArgumentTypes[0]);
                }
                catch (Exception ex)
                {
                    throw new BadInputFormatException(ex);
                }

                var result = await handlerInstance.Invoke(keyParam);
                if (result == null)
                    return NotFound();
                return lookup ? StatusCode(206, result) : HandleResult(result);
            }
            
            

            if (handlerInfo.NormalizedInterfaceType == typeof(IDeleteHandler<,>))
            {
                object keyParam;
                try
                {
                    keyParam = key.ConvertValueToType(handlerInfo.ArgumentTypes[0]);
                }
                catch (Exception ex)
                {
                    throw new BadInputFormatException(ex);
                }

                await handlerInstance.Invoke(keyParam);
                return Accepted();
            }

            return NotFound();
        }

        private IActionResult HandleResult(object result)
        {
            switch (result)
            {
                case ICqApiResult cqApiResult:
                {
                    foreach (var kvp in cqApiResult.Headers)
                        Response.Headers.Add(kvp.Key, kvp.Value);

                    switch (cqApiResult.Status)
                    {
                        case CqApiResultStatus.UnderProcessing:
                            return Accepted(cqApiResult.Result.ToString());
                        case CqApiResultStatus.ChangedLocation:
                            return RedirectPermanent(cqApiResult.Result.ToString() ?? string.Empty);
                        case CqApiResultStatus.Ok:
                        case CqApiResultStatus.Error:
                        default:
                        {
                            switch (cqApiResult.Result)
                            {
                                case null:
                                    return NoContent();
                                case string stringResult:
                                    return new ContentResult
                                    {
                                        StatusCode = cqApiResult.Status == CqApiResultStatus.Ok ? 200 : 400,
                                        Content = stringResult,
                                        ContentType = cqApiResult.ContentType,
                                    };
                                case byte[] bytes:
                                {
                                    return new FileContentResult(bytes, cqApiResult.ContentType);
                                }
                            }

                            break;
                        }
                    }

                    break;
                }
                case null:
                    return NoContent();
            }

            var res = SendOkResult(result);
            return res;
        }
    }
}