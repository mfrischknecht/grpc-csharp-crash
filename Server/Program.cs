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
		private sealed class TaskQueue<T>
		{
			private sealed class Item
			{
				public T Value { get; set; }
				public Task<Item> Next { get; set; }
			}

			private int m_size = 0;
			private TaskCompletionSource<Item> m_putSource;
			private Task<Item> m_takeTask;

			public TaskQueue()
			{
				m_putSource = new TaskCompletionSource<Item>();
				m_takeTask = m_putSource.Task;
			}

			public void Put(T item)
			{
				if (Interlocked.Increment(ref m_size) > 100)
				{
					Interlocked.Decrement(ref m_size);
					throw new InvalidOperationException("Queue is full");
				}

				var newSource = new TaskCompletionSource<Item>();
				var newItem = new Item { Next = newSource.Task, Value = item };

				var putSource = Interlocked.Exchange(ref m_putSource, newSource);
				putSource.SetResult(newItem);
			}

			public async Task<T> Take(CancellationToken? cancel = null)
			{
				var cxl = cancel ?? CancellationToken.None;
				var task = m_takeTask;
				var item = await task.ContinueWith(i => i.Result, cxl);
				var swapped = Interlocked.CompareExchange(ref m_takeTask, item.Next, task);
				Interlocked.Decrement(ref m_size);
				return item.Value;
			}
		}

		private sealed class Subscription
		{
			private static int s_nextID = 0;
			private static int GenerateID() { return Interlocked.Increment(ref s_nextID); }

			public int ID { get; }
			public BufferBlock<PriceUpdate> Queue { get; }

			public Subscription()
			{
				ID = GenerateID();
				Queue = new BufferBlock<PriceUpdate>();
			}
		}

		private sealed class Service : Pricefeed.PricefeedBase
		{
			private readonly object m_lock = new object();
			private ImmutableDictionary<int,Subscription> m_subscriptions = ImmutableDictionary.Create<int,Subscription>();
			public ImmutableDictionary<int,Subscription> Subscriptions { get { return m_subscriptions; } }

			public async override Task Subscribe(PriceUpdateSubscription request, IServerStreamWriter<PriceUpdate> responseStream, ServerCallContext context)
			{
				var subscription = new Subscription();

				lock (m_lock)
					m_subscriptions = m_subscriptions.Add(subscription.ID, subscription);

				//var data = CreateUpdateMessage(60);
				var data = CreateUpdateMessage(1);

				try
				{
					var cancel = context.CancellationToken;
					while (!cancel.IsCancellationRequested)
					{
						//var data = await subscription.Queue.ReceiveAsync(cancel);
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

		private static async Task GenerateTraffic(Service service, CancellationToken cancel)
		{
			try
			{
				var update = CreateUpdateMessage(60);
				var waitTime = TimeSpan.FromMilliseconds(1);
				while (!cancel.IsCancellationRequested)
				{
					//await Task.Delay(waitTime);

					var subscriptions = service.Subscriptions;
					if (!subscriptions.Any()) continue;

					var tasks = 
						subscriptions.Values
							.Select(s => s.Queue.SendAsync(update))
							.ToArray();

					await Task.WhenAll(tasks);
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				throw new RpcException(new Status(StatusCode.Internal, ex.Message));
			}
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
			//var task = Task.Run(() => GenerateTraffic(service, cancel.Token));

			Console.WriteLine("Server is running!");
			Console.ReadLine();
			cancel.Cancel();
			server.ShutdownAsync().Wait();
		}
	}
}
