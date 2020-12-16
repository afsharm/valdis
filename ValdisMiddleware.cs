using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Valdis
{
    public class ValidsMiddleware
    {
        private readonly RequestDelegate _next;
        private const string CDN_HEADER_NAME = "Cache-Control";
        private static readonly string[] NotForwardedHttpHeaders = new[] { "Connection", "Host" };
        private readonly ILogger<ValidsMiddleware> _logger;
        private readonly List<Provider> _providers;
        private readonly Random _random;

        public ValidsMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ValidsMiddleware> logger)
        {
            _next = next;
            var settings = configuration.GetSection("AppSettings:Providers");
            _providers = settings.Get<List<Provider>>();
            _random = new Random(DateTime.Now.Millisecond);
            _logger = logger;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            var baseTarget = string.Empty;
            var path = httpContext.Request.Path.Value;

            foreach (var provider in _providers)
            {
                if (path.StartsWith(provider.Starting))
                {
                    var index = _random.Next(provider.Hosts.Count);
                    baseTarget = provider.Hosts[index];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(baseTarget))
            {
                _logger.LogInformation($"Address '{path}' not found");
                return;
            }

            var targetUri = new Uri($"{baseTarget}{path}");
            var requestMessage = GenerateProxifiedRequest(httpContext, targetUri);
            await SendAsync(httpContext, requestMessage);
        }

        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler()
        {
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = int.MaxValue,
            UseCookies = false,
        });

        private async Task SendAsync(HttpContext context, HttpRequestMessage requestMessage)
        {
            using (var responseMessage = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
            {
                context.Response.StatusCode = (int)responseMessage.StatusCode;

                foreach (var header in responseMessage.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                foreach (var header in responseMessage.Content.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                context.Response.Headers.Remove("transfer-encoding");

                if (!context.Response.Headers.ContainsKey(CDN_HEADER_NAME))
                {
                    context.Response.Headers.Add(CDN_HEADER_NAME, "no-cache, no-store");
                }

                await responseMessage.Content.CopyToAsync(context.Response.Body);
            }
        }

        private static HttpRequestMessage GenerateProxifiedRequest(HttpContext context, Uri targetUri)
        {
            var requestMessage = new HttpRequestMessage();
            CopyRequestContentAndHeaders(context, requestMessage);

            requestMessage.RequestUri = targetUri;
            requestMessage.Headers.Host = targetUri.Host;
            requestMessage.Method = GetMethod(context.Request.Method);

            return requestMessage;
        }

        private static void CopyRequestContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
        {
            var requestMethod = context.Request.Method;
            if (!HttpMethods.IsGet(requestMethod) &&
                !HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) &&
                !HttpMethods.IsTrace(requestMethod))
            {
                var streamContent = new StreamContent(context.Request.Body);
                requestMessage.Content = streamContent;
            }

            foreach (var header in context.Request.Headers)
            {
                if (!NotForwardedHttpHeaders.Contains(header.Key))
                {
                    if (header.Key != "User-Agent")
                    {
                        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                        {
                            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                        }
                    }
                    else
                    {
                        string userAgent = header.Value.Count > 0 ? (header.Value[0] + " " + context.TraceIdentifier) : string.Empty;

                        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, userAgent) && requestMessage.Content != null)
                        {
                            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, userAgent);
                        }
                    }

                }
            }
        }

        private static HttpMethod GetMethod(string method)
        {
            if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
            if (HttpMethods.IsGet(method)) return HttpMethod.Get;
            if (HttpMethods.IsHead(method)) return HttpMethod.Head;
            if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
            if (HttpMethods.IsPost(method)) return HttpMethod.Post;
            if (HttpMethods.IsPut(method)) return HttpMethod.Put;
            if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
            return new HttpMethod(method);
        }
    }

    public static class ValidsMiddlewareExtensions
    {
        public static IApplicationBuilder UseValdis(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ValidsMiddleware>();
        }
    }

    public class Provider
    {
        public string Starting { set; get; }
        public List<string> Hosts { set; get; }
    }
}
