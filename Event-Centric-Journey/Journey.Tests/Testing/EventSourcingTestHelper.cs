﻿using Journey.EventSourcing;
using Journey.Messaging;
using Journey.Messaging.Logging.Metadata;
using Journey.Messaging.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Journey.Tests.Testing
{
    public class EventSourcingTestHelper<T> where T : IEventSourced
    {
        private ICommandHandler handler;
        private IEventHandler eventHandler;
        private readonly EventStoreStub store;
        private string expectedCorrelationId;

        public EventSourcingTestHelper()
        {
            this.Events = new List<IVersionedEvent>();
            this.store =
                new EventStoreStub((eventSourced, correlationId, dateTime) =>
            {
                if (this.expectedCorrelationId != null)
                    Assert.Equal(this.expectedCorrelationId, correlationId);

                this.Events.AddRange(eventSourced.Events);
            });
        }

        /// <summary>
        /// Los eventos que se han levantado desde que procesó comandos.
        /// </summary>
        public List<IVersionedEvent> Events { get; private set; }

        public IEventStore<T> Store { get { return this.store; } }

        /// <summary>
        /// El historial de eventos que estaba persistido antes de que se procesaran comandos.
        /// </summary>
        public List<IVersionedEvent> History { get { return this.store.History; } }

        public void Setup(ICommandHandler handler)
        {
            this.handler = handler;
        }

        public void Setup(IEventHandler handler)
        {
            this.eventHandler = handler;
        }

        public void Given(params IVersionedEvent[] history)
        {
            this.store.History.AddRange(history);
        }

        public void When(ICommand command)
        {
            this.expectedCorrelationId = command.Id.ToString();
            ((dynamic)this.handler).Handle((dynamic)command);
            this.expectedCorrelationId = null;
        }

        public void When(IEvent @event)
        {
            if (this.handler != null)
                ((dynamic)this.handler).Handle((dynamic)@event);
            else
                ((dynamic)this.eventHandler).Handle((dynamic)@event);
        }

        public bool ThenContains<TEvent>() where TEvent : IVersionedEvent
        {
            return this.Events.Any(x => x.GetType() == typeof(TEvent));
        }

        public TEvent ThenHasSingle<TEvent>() where TEvent : IVersionedEvent
        {
            Assert.Equal(1, this.Events.Count);
            var @event = this.Events.Single();
            Assert.IsAssignableFrom<TEvent>(@event);
            return (TEvent)@event;
        }

        public TEvent ThenHasOne<TEvent>() where TEvent : IVersionedEvent
        {
            Assert.Equal(1, this.Events.OfType<TEvent>().Count());
            var @event = this.Events.OfType<TEvent>().Single();
            return @event;
        }

        private class EventStoreStub : IEventStore<T>
        {
            public readonly List<IVersionedEvent> History = new List<IVersionedEvent>();
            private readonly Action<T, string, DateTime> onSave;
            private readonly Func<Guid, IEnumerable<IVersionedEvent>, T> entityFactory;
            private readonly IMetadataProvider metadataProvider;

            internal EventStoreStub(Action<T, string, DateTime> onSave)
            {
                this.onSave = onSave;
                this.metadataProvider = new StandardMetadataProvider();
                var constructor = typeof(T).GetConstructor(new[] { typeof(Guid), typeof(IEnumerable<IVersionedEvent>) });
                if (constructor == null)
                {
                    throw new InvalidCastException(
                        "Type T must have a constructor with the following signature: .ctor(Guid, IEnumerable<IVersionedEvent>)");
                }
                this.entityFactory = (id, events) => (T)constructor.Invoke(new object[] { id, events });
            }

            T IEventStore<T>.Find(Guid id)
            {
                var all = this.History.Where(x => x.SourceId == id).ToList();
                if (all.Count > 0)
                    return this.entityFactory.Invoke(id, all);

                return default(T);
            }

            private void Save(T eventSourced, Guid correlationId, DateTime dateTime)
            {
                this.onSave(eventSourced, correlationId.ToString(), dateTime);
            }

            void IEventStore<T>.Save(T eventSourced, IMessage message)
            {
                var metadata = this.metadataProvider.GetMetadata(message);

                switch (metadata[StandardMetadata.Kind])
                {
                    case StandardMetadata.EventKind:
                        this.Save(eventSourced, ((IVersionedEvent)message).CorrelationId, message.CreationDate);
                        break;

                    case StandardMetadata.CommandKind:
                        this.Save(eventSourced, ((ICommand)message).Id, message.CreationDate);
                        break;
                }
            }

            T IEventStore<T>.Get(Guid id)
            {
                var entity = ((IEventStore<T>)this).Find(id);
                if (EventStoreStub.Equals(entity, default(T)))
                    throw new EntityNotFoundException(id, "Test");

                return entity;
            }
        }
    }
}
