﻿using Journey.EventSourcing.ReadModeling;
using System;
using System.Data.Entity;

namespace Journey.Tests.Integration.EventSourcing.ReadModeling
{
    public class ItemReadModelDbContext : ReadModelDbContext
    {
        public ItemReadModelDbContext(string connectionString)
            : base(connectionString)
        { }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Item>()
                .ToTable("Items", "Domain")
                .HasKey(i => i.UnidentifiableId);

            modelBuilder.Entity<MailingSubscription>()
                .ToTable("Mailing", "SubscriptionLog")
                .HasKey(l => new { SourceId = l.SourceId, SourceType = l.SourceType });
        }

        public IDbSet<Item> Items { get; set; }

        public IDbSet<MailingSubscription> Mailing { get; set; }
    }

    public class Item
    {
        public int UnidentifiableId { get; set; }
        public string Name { get; set; }
    }

    public class MailingSubscription : IProcessedEvent
    {
        public Guid SourceId { get; set; }

        public string SourceType { get; set; }

        public int Version { get; set; }

        public string EventType { get; set; }

        public Guid CorrelationId { get; set; }
    }
}
