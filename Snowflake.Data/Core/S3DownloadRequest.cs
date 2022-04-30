﻿/*
 * Copyright (c) 2012-2021 Snowflake Computing Inc. All rights reserved.
 */

namespace Tortuga.Data.Snowflake.Core;

internal class S3DownloadRequest : BaseRestRequest, IRestRequest
{
	private const string SSE_C_ALGORITHM = "x-amz-server-side-encryption-customer-algorithm";

	private const string SSE_C_KEY = "x-amz-server-side-encryption-customer-key";

	private const string SSE_C_AES = "AES256";

	internal string qrmk { get; set; }

	internal Dictionary<string, string> chunkHeaders { get; set; }

	HttpRequestMessage IRestRequest.ToRequestMessage(HttpMethod method)
	{
		HttpRequestMessage message = newMessage(method, Url);
		if (chunkHeaders != null)
		{
			foreach (var item in chunkHeaders)
			{
				message.Headers.Add(item.Key, item.Value);
			}
		}
		else
		{
			message.Headers.Add(SSE_C_ALGORITHM, SSE_C_AES);
			message.Headers.Add(SSE_C_KEY, qrmk);
		}

		return message;
	}
}
