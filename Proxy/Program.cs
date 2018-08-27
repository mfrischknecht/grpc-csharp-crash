using Grpc.Core;
using Grpctest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Proxy
{
	class Program
	{
		private sealed class Service : Pricefeed.PricefeedBase
		{
			private readonly Pricefeed.PricefeedClient m_client; 

			public Service(Pricefeed.PricefeedClient client)
			{
				if (client == null) throw new ArgumentNullException(nameof(client));
				m_client = client;
			}

			private async Task ReceiveUpdates(PriceUpdateSubscription request, ITargetBlock<PriceUpdate> output, CancellationToken cancel)
			{
				try
				{
					var httpRequest = m_client.Subscribe(request);
					var subscription = httpRequest.ResponseStream;
					while (await subscription.MoveNext(cancel))
					{
						var data = subscription.Current;
						await output.SendAsync(data, cancel);
					}
					throw new RpcException(httpRequest.GetStatus());
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine(ex);
					throw;
				}
			}

			private async Task SendUpdates(ISourceBlock<PriceUpdate> input, IServerStreamWriter<PriceUpdate> output, CancellationToken cancel)
			{
				try
				{
					while (true)
					{
						var data = await input.ReceiveAsync(cancel);
						await output.WriteAsync(data);
					}
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine(ex);
					throw;
				}
			}

			public async override Task Subscribe(PriceUpdateSubscription request, IServerStreamWriter<PriceUpdate> responseStream, ServerCallContext context)
			{
				try
				{
					var cancel = context.CancellationToken;
					var buffer = new BufferBlock<PriceUpdate>(
						new DataflowBlockOptions { BoundedCapacity = 1000 });
					var consumer = SendUpdates(buffer, responseStream, cancel);
					var producer = ReceiveUpdates(request, buffer, cancel);
					await Task.WhenAll(producer, consumer);

					//var cancel = context.CancellationToken;
					//var subscription = m_client.Subscribe(request);
					//while (await subscription.ResponseStream.MoveNext(cancel))
					//{
					//	var data = subscription.ResponseStream.Current;
					//	await responseStream.WriteAsync(data);
					//}
					//throw new RpcException(subscription.GetStatus());
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine(ex);
					throw;
				}
			}
		}

		static void Main(string[] args)
		{
			var channel = new Channel("127.0.0.1:12345", ChannelCredentials.Insecure);
			var client = new Pricefeed.PricefeedClient(channel);

			var service = new Service(client);
			var server = new Server {
				Services = { Pricefeed.BindService(service) },
				Ports = { new ServerPort("localhost", 12346, ServerCredentials.Insecure) },
			};

			server.Start();
			Console.WriteLine("Proxy is running!");
			Console.ReadLine();
			Console.WriteLine("Proxy is shutting down...");
			server.ShutdownAsync().Wait();
		}
	}
}
