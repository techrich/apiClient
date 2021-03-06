﻿using System;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SmartPlugin.ApiClient.Enums;
using SmartPlugin.ApiClient.Model;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using SmartPlugin.ApiClient.Attributes;
using SmartPlugin.ApiClient.Extensions;

namespace SmartPlugin.ApiClient
{
    public partial class ApiBaseClient : AbstractApiBaseClient
    {
        private System.Lazy<Newtonsoft.Json.JsonSerializerSettings> _settings;
        protected Newtonsoft.Json.JsonSerializerSettings JsonSerializerSettings { get { return _settings.Value; } }

        protected ApiBaseClient():base(){}

        protected ApiBaseClient(string route) : base(route){}

        protected ApiBaseClient(string baseUrl, string route):base(baseUrl, route) { }


        partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings);

        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url);
        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder);
        partial void ProcessResponse(HttpClient client, HttpResponseMessage response);

        protected override void InitializeClient()
        {
            _settings = new System.Lazy<Newtonsoft.Json.JsonSerializerSettings>(() =>
            {
                var settings = new Newtonsoft.Json.JsonSerializerSettings();
                UpdateJsonSerializerSettings(settings);
                return settings;
            });
        }

        /// <summary>
        /// Executes the asynchronous.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parameters">The parameters.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        protected async override Task<T> ExecuteAsync<T>(Parameters parameters, CancellationToken cancellationToken = default(CancellationToken))
        {
            var opInfo = new StackTrace().GetOperationInfo();

            HttpVerb action = opInfo.ActionInfo.Action;
            string actionTemplate = opInfo.ActionInfo.Template;
            return await ExecuteAsync<T>(action, actionTemplate, parameters, cancellationToken);
        }

        /// <summary>
        /// Executes the asynchronous.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The action.</param>
        /// <param name="actionTemplate">The action template.</param>
        /// <param name="parameters">The parameters.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <exception cref="ApiClientException">
        /// null
        /// </exception>
        protected async override Task<T> ExecuteAsync<T>(HttpVerb action, string actionTemplate, Parameters parameters,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var opInfo = new StackTrace().GetOperationInfo();
            var methodName = opInfo.MetaInfo.Name;
            var urlBuilder = GetRequestUrl(actionTemplate, parameters);

            var client = default(HttpClient);
            try
            {
                using (client = Config.WindowsAuthentication
                    ? new HttpClient(new HttpClientHandler() {UseDefaultCredentials = true})
                    : new HttpClient())
                {

                    using (var request = new HttpRequestMessage())
                    {
                        //Stuffing header parameters
                        if (parameters?.ContainsKey(BindingSource.Header) ?? false)
                            parameters[BindingSource.Header]?.ToList().ForEach(p =>
                            {
                                request.Headers.TryAddWithoutValidation(p.Name,
                                    ConvertToString(p.Value.ToString(), CultureInfo.InvariantCulture));
                            });

                        if ((ApiHeaders?.Count ?? 0) > 0)
                        {
                            ApiHeaders?.ToList().ForEach(h =>
                            {
                                request.Headers.TryAddWithoutValidation(h.Key,
                                    ConvertToString(h.Value, CultureInfo.InvariantCulture));
                            });
                        }
                        ////Stuffing body parameters
                        //if (parameters?.ContainsKey(BindingSource.Body) ?? false)
                        //{
                        //    if ((parameters[BindingSource.Body]?.Count ?? 0) > 1)
                        //        throw new ApiClientException(
                        //            $"Target of invocation exception in calling {methodName} due to an error: 'API call has multiple parameter bindings to the body element.'");

                        //    var p = parameters[BindingSource.Body]?.ToList().FirstOrDefault();
                        //    var content =
                        //        new StringContent(JsonConvert.SerializeObject(p.Value, _settings.Value));
                        //    content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                        //    request.Content = content;
                        //}

                        //Binding RequestConent which could be a form / body element
                        if (parameters.RequestContent != null)
                            request.Content = parameters.RequestContent;

                        //Setting Http action verb
                        request.Method = new HttpMethod(Enum.GetName(typeof(HttpVerb), action));
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                        PrepareRequest(client, request, urlBuilder);

                        var url = urlBuilder.ToString();

                        request.RequestUri = new Uri(url, UriKind.RelativeOrAbsolute);
                        PrepareRequest(client, request, url);

                        var response = await client
                            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                            .ConfigureAwait(false);

                        try
                        {
                            var headers = response?.Headers.ToDictionary(h => h.Key, h => h.Value);

                            response?.Content?.Headers?.ToList().ForEach(h => { headers[h.Key] = h.Value; });

                            ProcessResponse(client, response);

                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                var responseData = response.Content == null
                                    ? null
                                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                                var result = default(T);
                                try
                                {
                                    result = JsonConvert.DeserializeObject<T>(responseData, _settings.Value);
                                    return result;
                                }
                                catch (Exception ex)
                                {
                                    throw new ApiClientException(
                                        $"Target of invocation exception in calling {methodName} due to an error: \"Could not deserialize the response body.\"",
                                        (int) response.StatusCode, responseData, headers, ex);
                                }
                            }
                            else if (response.StatusCode != HttpStatusCode.OK &&
                                     response.StatusCode != HttpStatusCode.NoContent)
                            {
                                var responseData = response.Content == null
                                    ? null
                                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                throw new ApiClientException(
                                    $"Target of invocation exception in calling {methodName} due to an error: \"The HTTP status code of the response was not expected (\"{Enum.GetName(typeof(HttpStatusCode), response.StatusCode)}:{(int) response.StatusCode}).",
                                    (int) response.StatusCode, responseData, headers, null);
                            }

                            return default(T);
                        }
                        finally
                        {
                            if (response != null)
                                response.Dispose();
                        }
                    }
                }
            }
            finally
            {
                if (client != null)
                    client.Dispose();
            }
        }
    }
}
