using Grpctest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using System.Collections.Immutable;

using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using System.Threading.Tasks.Dataflow;

namespace Server
{
	class Program
	{
		private sealed class Service : Pricefeed.PricefeedBase
		{
			public async override Task Subscribe(PriceUpdateSubscription request, IServerStreamWriter<PriceUpdate> responseStream, ServerCallContext context)
			{
				var data = CreateUpdateMessage(1);

				try
				{
					var cancel = context.CancellationToken;
					while (!cancel.IsCancellationRequested)
					{
						await responseStream.WriteAsync(data);
					}
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine(ex);
				}
			}
		}

		private static PriceUpdate CreateUpdateMessage(int levels)
		{
			var rnd = new Random();

			var now = Timestamp.FromDateTime(DateTime.UtcNow);
			var update = new PriceUpdate {
				Timestamp = now,
				ClosingPrice = Math.Abs(rnd.NextDouble()),
				ClosingPriceTimestamp = now,
				LastPrice = Math.Abs(rnd.NextDouble()),
				LastPriceTimestamp = now,
			};

			update.PriceUpdates.AddRange(
					Enumerable.Range(0,levels)
						.Select(i => new PriceUpdate.Types.Level {
							Level_ = i,
							Bid = new PriceUpdate.Types.Side {
								Price = Math.Abs(rnd.NextDouble()),
								Quantity = Math.Abs(rnd.Next()),
								NoOrders = Math.Abs(rnd.Next()),
								NoQuotes = Math.Abs(rnd.Next()),
							},
							Ask = new PriceUpdate.Types.Side {
								Price = Math.Abs(rnd.NextDouble()),
								Quantity = Math.Abs(rnd.Next()),
								NoOrders = Math.Abs(rnd.Next()),
								NoQuotes = Math.Abs(rnd.Next()),
							},
						}));

			return update;
		}

		static void Main(string[] args)
		{
			var service = new Service();
			var server = new Grpc.Core.Server {
				Services = { Pricefeed.BindService(service) },
				Ports = { new ServerPort("localhost", 12345, ServerCredentials.Insecure) },
			};

			var cancel = new CancellationTokenSource();
			server.Start();

			Console.WriteLine("Server is running!");
			Console.ReadLine();
			cancel.Cancel();
			server.ShutdownAsync().Wait();
		}
	}
}
