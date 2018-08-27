using Grpc.Core;
using Grpctest;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
	class Program
	{
		static void Main(string[] args)
		{
			var channel = new Channel("127.0.0.1:12346", ChannelCredentials.Insecure);
			var client = new Pricefeed.PricefeedClient(channel);

			long m_receivedMessages = 0;

			var subscriptions = 
				Enumerable.Range(0,100)
					.Select(i => Task.Run(async () => {
						var subscription = client.Subscribe(new PriceUpdateSubscription());
						try
						{
							while (await subscription.ResponseStream.MoveNext())
							{
								var msg = subscription.ResponseStream.Current;
								Interlocked.Increment(ref m_receivedMessages);
							}
						}
						catch (Exception ex)
						{
							Console.Error.WriteLine(ex);
						}
					}))
					.ToArray();

			var printMessagesPerSecond = 
				Task.Run(async () => {
						var waitTime = TimeSpan.FromSeconds(3);
						var watch = new Stopwatch();
						watch.Start();
						while (true)
						{
							await Task.Delay(waitTime);
							var numMessages = Interlocked.Exchange(ref m_receivedMessages, 0);
							var elapsed = watch.Elapsed;
							watch.Restart();

							Console.WriteLine($"Updates per second: {numMessages/elapsed.TotalSeconds}");
						}
					});

			Console.WriteLine("Client is running!");
			Console.ReadLine();
			channel.ShutdownAsync().Wait();
		}
	}
}
