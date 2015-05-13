﻿using Journey.Database;
using Journey.EventSourcing.RebuildPerfCounting;
using Journey.Messaging;
using Journey.Messaging.Logging;
using Journey.Messaging.Logging.Metadata;
using Journey.Messaging.Processing;
using Journey.Serialization;
using Journey.Utils;
using Journey.Utils.SystemTime;
using Journey.Worker;
using Journey.Worker.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Journey.EventSourcing.EventStoreRebuilding
{
    public class EventStoreRebuilderEngine : IEventStoreRebuilderEngine
    {
        private readonly Func<EventStoreDbContext> eventStoreContextFactory;
        private readonly ITextSerializer serializer;
        private readonly IMetadataProvider metadataProvider;

        private readonly IEventStoreRebuilderConfig config;

        private readonly ITracer tracer;

        private readonly IInMemoryBus bus;

        private readonly IEventDispatcher eventDispatcher;
        private readonly ICommandProcessor commandProcessor;
        private readonly ICommandHandlerRegistry commandHandlerRegistry;

        private IMessageAuditLog auditLog;

        private MessageLogHandler handler;

        private readonly IRebuilderPerfCounter perfCounter;

        public EventStoreRebuilderEngine(
            IInMemoryBus bus,
            ICommandProcessor commandProcessor, ICommandHandlerRegistry commandHandlerRegistry, IEventDispatcher eventDispatcher,
            ITextSerializer serializer, IMetadataProvider metadataProvider,
            ITracer tracer,
            IEventStoreRebuilderConfig config,
            Func<EventStoreDbContext> eventStoreContextFactory,
            IRebuilderPerfCounter perfCounter)
        {
            this.bus = bus;
            this.eventStoreContextFactory = eventStoreContextFactory;
            this.serializer = serializer;
            this.eventDispatcher = eventDispatcher;
            this.commandProcessor = commandProcessor;
            this.commandHandlerRegistry = commandHandlerRegistry;
            this.config = config;
            this.tracer = tracer;
            this.metadataProvider = metadataProvider;
            this.perfCounter = perfCounter;
        }

        public void Rebuild()
        {
            var rowsAffected = default(int);

            this.perfCounter.OnStartingRebuildProcess(this.GetMessagesCount());
            this.perfCounter.OnOpeningDbConnectionAndCleaning();

            using (var eventStoreContext = this.eventStoreContextFactory.Invoke())
            {
                TransientFaultHandlingDbConfiguration.SuspendExecutionStrategy = true;

                using (var eventStoreTransaction = eventStoreContext.Database.BeginTransaction())
                {
                    try
                    {
                        eventStoreContext.Database.ExecuteSqlCommand(@"DELETE FROM [EventStore].[Events]
                                                                           DELETE FROM [EventStore].[Snapshots]");

                        using (var sourceContext = new MessageLogDbContext(config.SourceMessageLogConnectionString))
                        {
                            var messages = sourceContext.Set<MessageLogEntity>()
                                            .OrderBy(m => m.Id)
                                            .AsEnumerable()
                                            .Select(this.CreateMessage)
                                            .AsCachedAnyEnumerable();

                            using (var newAuditLogContext = new MessageLogDbContext(config.NewMessageLogConnectionString))
                            {
                                using (var auditLogTransaction = newAuditLogContext.Database.BeginTransaction())
                                {
                                    try
                                    {
                                        this.RegisterLogger(newAuditLogContext);

                                        this.perfCounter.OnDbConnectionOpenedAndCleansed();
                                        this.perfCounter.OnStartingStreamProcessing();

                                        this.ProcessMessages(messages);

                                        this.perfCounter.OnStreamProcessingFinished();
                                        this.perfCounter.OnStartingCommitting();

                                        // el borrado colocamos al final por si se este haciendo desde el mismo connection.
                                        newAuditLogContext.Database.ExecuteSqlCommand(@"
                                                DELETE FROM [MessageLog].[Messages]
                                                DBCC CHECKIDENT ('[MessageLog].[Messages]', RESEED, 0)");


                                        rowsAffected = +newAuditLogContext.SaveChanges();

                                        auditLogTransaction.Commit();
                                    }
                                    catch (Exception)
                                    {
                                        auditLogTransaction.Rollback();
                                        throw;
                                    }
                                }
                            }
                        }

                        rowsAffected = +eventStoreContext.SaveChanges();

                        eventStoreTransaction.Commit();

                        this.perfCounter.OnCommitted(rowsAffected);
                    }
                    catch (Exception)
                    {
                        eventStoreTransaction.Rollback();
                        throw;
                    }
                    finally
                    {
                        TransientFaultHandlingDbConfiguration.SuspendExecutionStrategy = false;
                    }
                }
            }
        }

        private int GetMessagesCount()
        {
            var sql = new SqlCommandWrapper(config.SourceMessageLogConnectionString);
            return sql.ExecuteReader(@"
                        select count(*) as RwCnt 
                        from MessageLog.Messages 
                        ", r => r.SafeGetInt32(0))
                         .FirstOrDefault();
        }

        private void RegisterLogger(MessageLogDbContext newContext)
        {
            this.auditLog = new InMemoryMessageLog(this.serializer, this.metadataProvider, this.tracer, newContext, new LocalDateTime());
            this.handler = new MessageLogHandler(this.auditLog);
            this.commandHandlerRegistry.Register(this.handler);
            this.eventDispatcher.Register(this.handler);
        }

        private void ProcessMessages(IEnumerable<MessageForDelivery> messages)
        {
            foreach (var message in messages)
            {
                var body = this.Deserialize(message.Body);

                var command = body as ICommand;
                if (command != null)
                    this.ProcessCommand(command);
                else
                    this.ProcessEvent(body as IEvent);
            }
        }

        private void ProcessCommand(ICommand command)
        {
            if (this.auditLog.IsDuplicateMessage(command))
                return;

            this.commandProcessor.ProcessMessage(command);
            this.ProcessInnerMessages();
        }

        private void ProcessInnerMessages()
        {
            if (this.bus.HasNewCommands)
                foreach (var command in bus.GetCommands())
                    this.ProcessCommand(command);

            if (this.bus.HasNewEvents)
                foreach (var @event in bus.GetEvents())
                    this.ProcessEvent(@event);
        }

        private void ProcessEvent(IEvent @event)
        {
            if (this.auditLog.IsDuplicateMessage(@event))
                return;

            this.eventDispatcher.DispatchMessage(@event, null, string.Empty, string.Empty);
            this.ProcessInnerMessages();
        }

        private MessageForDelivery CreateMessage(MessageLogEntity message)
        {
            return new MessageForDelivery(message.Payload);
        }

        private object Deserialize(string serializedPayload)
        {
            using (var reader = new StringReader(serializedPayload))
            {
                return this.serializer.Deserialize(reader);
            }
        }
    }
}
