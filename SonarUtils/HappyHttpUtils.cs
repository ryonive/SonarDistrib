﻿using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace SonarUtils
{
    public static class HappyHttpUtils
    {
        private const int DelayMsTick = 400;
        private static readonly SocketsHttpHandler s_sharedHandler = CreateHttpHandler();

        public static HttpClient CreateHttpClient(bool shared = false)
        {
            return new HttpClient(shared ? s_sharedHandler : CreateHttpHandler(), !shared);
        }

        public static SocketsHttpHandler CreateHttpHandler()
        {
            return new SocketsHttpHandler()
            {
                ConnectCallback = ConnectCallbackAsync
            };
        }

        public static HttpClient CreateRandomlyHappyClient()
        {
            return System.Random.Shared.NextDouble() < 0.5 ?
                new HttpClient() : CreateHttpClient();
        }


        public static SocketsHttpHandler CreateRandomlyHappyHandler()
        {
            return System.Random.Shared.NextDouble() < 0.5 ?
                new SocketsHttpHandler() : CreateHttpHandler();
        }

        private static async ValueTask<Stream> ConnectCallbackAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var semaphore = new SemaphoreSlim(1);

            var entries = new List<IPAddress>();
            var dnsTasks = new Task[] {
                PerformDnsAsync(entries, AddressFamily.InterNetwork, context, cts.Token),
                PerformDnsAsync(entries, AddressFamily.InterNetworkV6, context, cts.Token),
            };

            await Task.WhenAny(dnsTasks);
            if (entries.Count == 0) await Task.WhenAll(dnsTasks);

            _ = TickRunnerAsync(semaphore, cts.Token);

            var attempted = new HashSet<int>();
            var streamTasks = new List<Task<Stream>>();
            do
            {
                await semaphore.WaitAsync(cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); // Force processing to allow disposing
                await Task.Delay(1, cts.Token).ConfigureAwait(false);

                var streamTask = streamTasks.Find(task => task.IsCompletedSuccessfully);
                if (streamTask is not null)
                {
                    try { cts.Cancel(); } catch { /* Swallow */ }
                    foreach (var task in streamTasks.Where(task => task != streamTask && task.IsCompletedSuccessfully)) task.Result.Dispose();
                    return streamTask.Result;
                }

                lock (entries)
                {
                    var index = System.Random.Shared.Next(entries.Count);
                    if (attempted.Add(index))
                    {
                        var entry = entries[index];
                        streamTasks.Add(CreateStreamAsync(entry, semaphore, context, cancellationToken));
                    }
                }

                await Task.Delay(1, cts.Token).ConfigureAwait(false);
            }
            while (attempted.Count < entries.Count || Array.FindIndex(dnsTasks, task => !task.IsCompleted) != -1 || streamTasks.FindIndex(task => !task.IsCompleted) != -1);
            
            cancellationToken.ThrowIfCancellationRequested();

            var exceptions = dnsTasks.Concat(streamTasks).Where(task => task.IsFaulted && !task.IsCanceled).Select(task => task.Exception).ToArray();
            AggregateException? aex = null;
            if (exceptions.Length > 0) aex = new(exceptions!);
            throw new HappySocketException($"Unable to happily connect to {context.DnsEndPoint.Host}:{context.DnsEndPoint.Port} (Resolved addresses: {string.Join(", ", entries)})", aex);
        }

        private static async Task PerformDnsAsync(List<IPAddress> entries, AddressFamily family, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var results = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, family, cancellationToken).ConfigureAwait(false);
            lock (entries)
            {
                foreach (var address in results)
                {
                    if (entries.Count >= 256) break;
                    if (!entries.Contains(address)) entries.Add(address);
                }
            }
        }

        private static async Task<Stream> CreateStreamAsync(IPAddress entry, SemaphoreSlim semaphore, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var socket = new Socket(entry.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(entry, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            semaphore.Release();
            return new NetworkStream(socket, true);
        }

        private static async Task TickRunnerAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(DelayMsTick, cancellationToken).ConfigureAwait(false);
                    if (semaphore.CurrentCount == 0) semaphore.Release();
                }
            }
            catch { /* Swallow */ }
        }
    }
}
