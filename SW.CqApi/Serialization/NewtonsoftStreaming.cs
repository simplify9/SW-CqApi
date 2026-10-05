using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;

namespace SW.CqApi.Serialization
{
    /// <summary>
    /// Reads request bodies and writes responses with Newtonsoft without materialising the
    /// JSON as a string. Same approach as MVC's own Newtonsoft formatters: Newtonsoft only does
    /// synchronous I/O, so the body is buffered first (in memory up to a small threshold, then
    /// in a temp file) and the sync reader/writer works against that buffer.
    /// </summary>
    internal static class NewtonsoftStreaming
    {
        // Kestrel disallows sync I/O, so a large body must be buffered before Newtonsoft reads it.
        // Above this size the buffer spills to a temp file instead of the managed heap.
        private const int MemoryBufferThreshold = 30 * 1024;

        public static async Task<object> ReadJsonAsync(this HttpRequest request, JsonSerializer serializer, Type type)
        {
            if (!request.Body.CanSeek)
            {
                request.EnableBuffering(MemoryBufferThreshold);
                await request.Body.DrainAsync(request.HttpContext.RequestAborted);
                request.Body.Seek(0, SeekOrigin.Begin);
            }

            using var streamReader = new HttpRequestStreamReader(request.Body, Encoding.UTF8);
            using var jsonReader = new JsonTextReader(streamReader)
            {
                ArrayPool = JsonCharArrayPool.Instance,
                CloseInput = false
            };

            return serializer.Deserialize(jsonReader, type);
        }

        public static async Task WriteJsonAsync(this HttpResponse response, JsonSerializer serializer, object value)
        {
            response.ContentType = "application/json";

            // Serialise into a buffer first so a serialisation failure can still become a
            // proper error response instead of a truncated 200.
            await using var buffer = new FileBufferingWriteStream();
            await using (var streamWriter = new HttpResponseStreamWriter(buffer, new UTF8Encoding(false)))
            {
                using var jsonWriter = new JsonTextWriter(streamWriter)
                {
                    ArrayPool = JsonCharArrayPool.Instance,
                    CloseOutput = false,
                    AutoCompleteOnClose = false
                };
                serializer.Serialize(jsonWriter, value);
                jsonWriter.Flush();
            }

            response.ContentLength = buffer.Length;
            await buffer.DrainBufferAsync(response.Body, response.HttpContext.RequestAborted);
        }
    }

    /// <summary>Writes a value with the CqApi serializer, streamed. Replaces <c>Content(string)</c>.</summary>
    internal sealed class NewtonsoftJsonResult : IActionResult
    {
        private readonly object value;
        private readonly JsonSerializer serializer;

        public NewtonsoftJsonResult(object value, JsonSerializer serializer)
        {
            this.value = value;
            this.serializer = serializer;
        }

        public Task ExecuteResultAsync(ActionContext context) =>
            context.HttpContext.Response.WriteJsonAsync(serializer, value);
    }

    internal sealed class JsonCharArrayPool : IArrayPool<char>
    {
        public static readonly JsonCharArrayPool Instance = new();

        public char[] Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);

        public void Return(char[] array) => ArrayPool<char>.Shared.Return(array);
    }
}
