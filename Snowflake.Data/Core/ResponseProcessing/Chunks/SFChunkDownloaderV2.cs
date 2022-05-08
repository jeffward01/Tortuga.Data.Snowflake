﻿/*
 * Copyright (c) 2012-2019 Snowflake Computing Inc. All rights reserved.
 */

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Tortuga.Data.Snowflake.Core.RequestProcessing;

namespace Tortuga.Data.Snowflake.Core.ResponseProcessing.Chunks;

class SFChunkDownloaderV2 : IChunkDownloader
{
	private List<SFResultChunk> chunks;

	private string qrmk;

	// External cancellation token, used to stop donwload
	private CancellationToken externalCancellationToken;

	//TODO: parameterize prefetch slot
	private const int prefetchSlot = 5;

	private readonly IRestRequester _RestRequester;

	private Dictionary<string, string> chunkHeaders;

	public SFChunkDownloaderV2(int colCount, List<ExecResponseChunk> chunkInfos, string qrmk,
		Dictionary<string, string> chunkHeaders, CancellationToken cancellationToken,
		IRestRequester restRequester)
	{
		this.qrmk = qrmk;
		this.chunkHeaders = chunkHeaders;
		this.chunks = new List<SFResultChunk>();
		_RestRequester = restRequester;
		externalCancellationToken = cancellationToken;

		var idx = 0;
		foreach (ExecResponseChunk chunkInfo in chunkInfos)
		{
			this.chunks.Add(new SFResultChunk(chunkInfo.url, chunkInfo.rowCount, colCount, idx++));
		}

		FillDownloads();
	}

	private BlockingCollection<Lazy<Task<IResultChunk>>> _downloadTasks;
	private ConcurrentQueue<Lazy<Task<IResultChunk>>> _downloadQueue;

	private void RunDownloads()
	{
		try
		{
			while (_downloadQueue.TryDequeue(out var task) && !externalCancellationToken.IsCancellationRequested)
			{
				if (!task.IsValueCreated)
				{
					task.Value.Wait(externalCancellationToken);
				}
			}
		}
		catch
		{
			//Don't blow from background threads.
		}
	}

	private void FillDownloads()
	{
		_downloadTasks = new BlockingCollection<Lazy<Task<IResultChunk>>>();

		foreach (var c in chunks)
		{
			var t = new Lazy<Task<IResultChunk>>(() => DownloadChunkAsync(new DownloadContextV2()
			{
				chunk = c,
				chunkIndex = c.ChunkIndex,
				qrmk = this.qrmk,
				chunkHeaders = this.chunkHeaders,
				cancellationToken = this.externalCancellationToken,
			}));

			_downloadTasks.Add(t);
		}

		_downloadTasks.CompleteAdding();

		_downloadQueue = new ConcurrentQueue<Lazy<Task<IResultChunk>>>(_downloadTasks);

		for (var i = 0; i < prefetchSlot && i < chunks.Count; i++)
			Task.Run(new Action(RunDownloads));
	}

	public Task<IResultChunk> GetNextChunkAsync()
	{
		if (_downloadTasks.IsAddingCompleted)
		{
			return Task.FromResult<IResultChunk>(null);
		}
		else
		{
			return _downloadTasks.Take().Value;
		}
	}

	private async Task<IResultChunk> DownloadChunkAsync(DownloadContextV2 downloadContext)
	{
		SFResultChunk chunk = downloadContext.chunk;

		chunk.downloadState = DownloadState.IN_PROGRESS;

		S3DownloadRequest downloadRequest = new S3DownloadRequest()
		{
			Url = new UriBuilder(chunk.url).Uri,
			qrmk = downloadContext.qrmk,
			// s3 download request timeout to one hour
			RestTimeout = TimeSpan.FromHours(1),
			HttpTimeout = TimeSpan.FromSeconds(16),
			chunkHeaders = downloadContext.chunkHeaders
		};

		Stream stream = null;
		using (var httpResponse = await _RestRequester.GetAsync(downloadRequest, downloadContext.cancellationToken).ConfigureAwait(false))
		using (stream = await httpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
		{
			if (httpResponse.Content.Headers.TryGetValues("Content-Encoding", out var encoding))
			{
				if (string.Equals(encoding.First(), "gzip", StringComparison.OrdinalIgnoreCase))
				{
					stream = new GZipStream(stream, CompressionMode.Decompress);
				}
			}

			parseStreamIntoChunk(stream, chunk);
		}

		chunk.downloadState = DownloadState.SUCCESS;

		return chunk;
	}

	/// <summary>
	///     Content from s3 in format of
	///     ["val1", "val2", null, ...],
	///     ["val3", "val4", null, ...],
	///     ...
	///     To parse it as a json, we need to preappend '[' and append ']' to the stream
	/// </summary>
	/// <param name="content"></param>
	/// <param name="resultChunk"></param>
	private static void parseStreamIntoChunk(Stream content, SFResultChunk resultChunk)
	{
		Stream openBracket = new MemoryStream(Encoding.UTF8.GetBytes("["));
		Stream closeBracket = new MemoryStream(Encoding.UTF8.GetBytes("]"));

		Stream concatStream = new ConcatenatedStream(new Stream[3] { openBracket, content, closeBracket });

		IChunkParser parser = ChunkParserFactory.GetParser(concatStream);
		parser.ParseChunk(resultChunk);
	}
}