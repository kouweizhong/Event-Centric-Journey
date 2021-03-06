﻿using Journey.Database;
using Journey.EventSourcing;
using Journey.Messaging;
using Journey.Messaging.Logging.Metadata;
using Journey.Serialization;
using Journey.Tests.Integration.EventSourcing.Helpers;
using Journey.Utils.SystemTime;
using Journey.Worker;
using Moq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using Xunit;

namespace Journey.Tests.Integration.EventSourcing
{
    public class InMemoryEventStoreFixture
    {
        public class GIVEN_store_and_bus_with_an_aggregate_in_memory : IDisposable
        {
            internal readonly string dbName;
            internal readonly string connectionString;
            protected readonly IInMemoryBus bus;
            internal IEventStore<FakeItemsAggregate> sut;
            internal ITextSerializer serializer;
            internal Mock<ISnapshotProvider> cacheMock = new Mock<ISnapshotProvider>();
            internal Guid aggregateId;


            public GIVEN_store_and_bus_with_an_aggregate_in_memory()
            {
                DbConfiguration.SetConfiguration(new TransientFaultHandlingDbConfiguration());

                this.serializer = CreateSerializer();
                this.dbName = typeof(EventStoreFixture).Name;
                var connectionFactory = System.Data.Entity.Database.DefaultConnectionFactory;

                this.connectionString = connectionFactory.CreateConnection(this.dbName).ConnectionString;

                // *********************************
                // EN FECOPROD:

                this.connectionString = string.Format("server=(local);Database={0};User Id=sa;pwd =123456", this.dbName);

                // BORRAR CUANDO SEA NECESARIO
                //***********************************

                using (var context = new EventStoreDbContext(this.connectionString))
                {
                    if (context.Database.Exists())
                        context.Database.Delete();

                    context.Database.Create();
                }

                this.bus = new InMemoryBus();
                MessagingDbInitializer.CreateDatabaseObjects(this.connectionString, "Bus");
            }

            [Fact]
            public void WHEN_saving_aggregate_with_just_one_event_THEN_can_rehydrate()
            {
                using (var context = new EventStoreDbContext(this.connectionString))
                {
                    this.sut = new InMemoryEventStore<FakeItemsAggregate>(this.bus, this.serializer, context, new ConsoleTracer(), new LocalDateTime(), new InMemorySnapshotProvider("test", new LocalDateTime()), new StandardMetadataProvider());

                    this.aggregateId = Guid.NewGuid();

                    var item = new Item { Id = 1, Name = "item1" };
                    var aggregate = new FakeItemsAggregate(aggregateId);
                    aggregate.AddItem(item.Id, item.Name, 10);

                    this.sut.Save(aggregate, new FakeCommand(this.aggregateId));

                    var retrivedAggregate = this.sut.Find(aggregateId);
                    Assert.NotNull(retrivedAggregate);

                    Assert.True(retrivedAggregate.itemsQuantity.ContainsKey(item.Id));
                    Assert.Equal(retrivedAggregate.itemsQuantity[item.Id], 10);
                }
            }

            [Fact]
            public void WHEN_saving_aggregate_with_a_batch_of_events_THEN_can_rehydrate()
            {
                using (var context = new EventStoreDbContext(this.connectionString))
                {
                    this.sut = new InMemoryEventStore<FakeItemsAggregate>(this.bus, this.serializer, context, new ConsoleTracer(), new LocalDateTime(), new InMemorySnapshotProvider("test", new LocalDateTime()), new StandardMetadataProvider());
                    
                    this.aggregateId = Guid.NewGuid();

                    var item = new Item { Id = 1, Name = "item1" };
                    var item2 = new Item { Id = 2, Name = "item2" };

                    var aggregate = new FakeItemsAggregate(aggregateId);

                    aggregate.AddItem(item.Id, item.Name, 10);
                    aggregate.AddItem(item2.Id, item2.Name, 10);
                    aggregate.AddItem(item.Id, item.Name, 5);

                    this.sut.Save(aggregate, new FakeCommand(Guid.Empty));

                    var retrivedAggregate = this.sut.Find(aggregateId);

                    Assert.NotNull(retrivedAggregate);
                    Assert.True(retrivedAggregate.itemsQuantity.ContainsKey(item.Id));
                    Assert.True(retrivedAggregate.itemsQuantity.ContainsKey(item2.Id));
                    Assert.Equal(retrivedAggregate.itemsQuantity[item2.Id], 10);
                    Assert.Equal(retrivedAggregate.itemsQuantity[item.Id], 15);
                }
            }

            [Fact]
            public void WHEN_retrieving_persisted_aggregate_with_a_batch_of_events_THEN_can_save_it_once_again_with_new_events_and_replay_it_again()
            {
                using (var context = new EventStoreDbContext(this.connectionString))
                {
                    this.sut = new InMemoryEventStore<FakeItemsAggregate>(this.bus, this.serializer, context, new ConsoleTracer(), new LocalDateTime(), new InMemorySnapshotProvider("test", new LocalDateTime()), new StandardMetadataProvider());
                    this.aggregateId = Guid.NewGuid();

                    var item = new Item { Id = 1, Name = "item1" };
                    var item2 = new Item { Id = 2, Name = "item2" };

                    var aggregate = new FakeItemsAggregate(aggregateId);

                    aggregate.AddItem(item.Id, item.Name, 10);
                    aggregate.AddItem(item2.Id, item2.Name, 10);
                    aggregate.AddItem(item.Id, item.Name, 5);

                    this.sut.Save(aggregate, new FakeCommand(Guid.Empty));

                    var retrivedAggregate = this.sut.Find(aggregateId);

                    Assert.NotNull(retrivedAggregate);
                    Assert.True(retrivedAggregate.itemsQuantity.ContainsKey(item.Id));
                    Assert.True(retrivedAggregate.itemsQuantity.ContainsKey(item2.Id));
                    Assert.Equal(retrivedAggregate.itemsQuantity[item2.Id], 10);
                    Assert.Equal(retrivedAggregate.itemsQuantity[item.Id], 15);

                    retrivedAggregate.RemoveItem(item2.Id, 7);
                    retrivedAggregate.RemoveItem(item.Id, 2);

                    this.sut.Save(retrivedAggregate, new FakeCommand(Guid.Empty)); 

                    var overRetrivedAggregate = this.sut.Find(aggregateId);

                    Assert.NotNull(overRetrivedAggregate);
                    Assert.True(overRetrivedAggregate.itemsQuantity.ContainsKey(item.Id));
                    Assert.True(overRetrivedAggregate.itemsQuantity.ContainsKey(item2.Id));
                    Assert.Equal(overRetrivedAggregate.itemsQuantity[item2.Id], 3);
                    Assert.Equal(overRetrivedAggregate.itemsQuantity[item.Id], 13);
                }
            }

            private static ITextSerializer CreateSerializer()
            {
                return new JsonTextSerializer();
            }

            public void Dispose()
            {
                var builder = new SqlConnectionStringBuilder(this.connectionString);
                builder.InitialCatalog = "master";
                builder.AttachDBFilename = string.Empty;

                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            string.Format(
                                CultureInfo.InvariantCulture,
                                @"
USE master
IF EXISTS (SELECT name FROM sys.databases WHERE name = N'{0}') 
ALTER DATABASE [{0}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE 
DROP DATABASE [{0}]
",
                                this.dbName);

                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        #region FakeItemsAggregateAndSaga

        public class FakeItemsAggregate : EventSourced,
            IRehydratesFrom<ItemAdded>,
            IRehydratesFrom<ItemRemoved>
        {
            public Dictionary<int, int> itemsQuantity = new Dictionary<int, int>();

            public FakeItemsAggregate(Guid id)
                : base(id)
            {
                //base.RehydratesFrom<ItemAdded>(this.OnItemAdded);
                //base.RehydratesFrom<ItemRemoved>(this.OnItemRemoved);
            }

            public FakeItemsAggregate(Guid id, IEnumerable<IVersionedEvent> history)
                : this(id)
            {
                this.LoadFrom(history);
            }

            public void AddItem(int id, string name, int quantity)
            {
                base.Update(new ItemAdded { SourceId = Guid.NewGuid(), Id = id, Name = name, Quantity = quantity });
            }

            public void RemoveItem(int id, int quantity)
            {
                base.Update(new ItemRemoved { SourceId = Guid.NewGuid(), Id = id, Quantity = quantity });
            }

            public void Rehydrate(ItemAdded e)
            {
                var incomingItemInfo = new Item { Id = e.Id, Name = e.Name };
                var newQuantityValue = e.Quantity;
                int quantity;
                if (this.itemsQuantity.TryGetValue(incomingItemInfo.Id, out quantity))
                {
                    newQuantityValue += quantity;
                }

                this.itemsQuantity[incomingItemInfo.Id] = newQuantityValue;
            }

            public void Rehydrate(ItemRemoved e)
            {
                var incomingItemInfo = new Item { Id = e.Id };
                var newQuantityValue = e.Quantity * -1;
                int quantity;
                if (this.itemsQuantity.TryGetValue(incomingItemInfo.Id, out quantity))
                {
                    newQuantityValue += quantity;
                }

                this.itemsQuantity[incomingItemInfo.Id] = newQuantityValue;
            }
        }

        public class Item
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class ItemAdded : InternalVersionedEvent
        {
            public ItemAdded()
            { }

            public int Id { get; set; }
            public string Name { get; set; }
            public int Quantity { get; set; }
        }

        public class ItemRemoved : InternalVersionedEvent
        {
            public ItemRemoved()
            { }

            public int Id { get; set; }
            public int Quantity { get; set; }
        }

        public class ItemReserved : InternalVersionedEvent
        {
            public ItemReserved()
            { }

            public Item Item { get; set; }
            public int Quantity { get; set; }
        }

        #endregion
    }
}
