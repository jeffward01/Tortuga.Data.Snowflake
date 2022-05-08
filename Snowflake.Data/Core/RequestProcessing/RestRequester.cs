﻿/*
 * Copyright (c) 2012-2019 Snowflake Computing Inc. All rights reserved.
 */

using Newtonsoft.Json;
using Tortuga.Data.Snowflake.Core.Messages;

namespace Tortuga.Data.Snowflake.Core.RequestProcessing;

internal class RestRequester : IRestRequester
{
	protected HttpClient _HttpClient;

	public RestRequester(HttpClient httpClient)
	{
		_HttpClient = httpClient;
	}

	public T Post<T>(IRestRequest request)
	{
		//Run synchronous in a new thread-pool task.
		return Task.Run(async () => await (PostAsync<T>(request, CancellationToken.None)).ConfigureAwait(false)).Result;
	}

	public async Task<T> PostAsync<T>(IRestRequest request, CancellationToken cancellationToken)
	{
		using (var response = await SendAsync(HttpMethod.Post, request, cancellationToken).ConfigureAwait(false))
		{
			var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			return JsonConvert.DeserializeObject<T>(json, JsonUtils.JsonSettings);
		}
	}

	public T Get<T>(IRestRequest request)
	{
		//Run synchronous in a new thread-pool task.
		return Task.Run(async () => await (GetAsync<T>(request, CancellationToken.None)).ConfigureAwait(false)).Result;
	}

	public async Task<T> GetAsync<T>(IRestRequest request, CancellationToken cancellationToken)
	{
		using (HttpResponseMessage response = await GetAsync(request, cancellationToken).ConfigureAwait(false))
		{
			var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			return JsonConvert.DeserializeObject<T>(json, JsonUtils.JsonSettings);
		}
	}

	public Task<HttpResponseMessage> GetAsync(IRestRequest request, CancellationToken cancellationToken)
	{
		return SendAsync(HttpMethod.Get, request, cancellationToken);
	}

	public HttpResponseMessage Get(IRestRequest request)
	{
		//Run synchronous in a new thread-pool task.
		return Task.Run(async () => await (GetAsync(request, CancellationToken.None)).ConfigureAwait(false)).Result;
	}

	private async Task<HttpResponseMessage> SendAsync(HttpMethod method,
													  IRestRequest request,
													  CancellationToken externalCancellationToken)
	{
		HttpRequestMessage message = request.ToRequestMessage(method);
		return await SendAsync(message, request.GetRestTimeout(), externalCancellationToken).ConfigureAwait(false);
	}

	protected virtual async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,
														  TimeSpan restTimeout,
														  CancellationToken externalCancellationToken)
	{
		// merge multiple cancellation token
		using (CancellationTokenSource restRequestTimeout = new CancellationTokenSource(restTimeout))
		{
			using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken,
			restRequestTimeout.Token))
			{
				HttpResponseMessage response = null;
				try
				{
					response = await _HttpClient
						.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
						.ConfigureAwait(false);
					response.EnsureSuccessStatusCode();

					return response;
				}
				catch (Exception e)
				{
					// Disposing of the response if not null now that we don't need it anymore
					response?.Dispose();
					throw restRequestTimeout.IsCancellationRequested ? new SnowflakeDbException(SFError.REQUEST_TIMEOUT) : e;
				}
			}
		}
	}
}