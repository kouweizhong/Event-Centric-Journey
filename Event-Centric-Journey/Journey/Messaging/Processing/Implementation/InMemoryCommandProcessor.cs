﻿using Journey.Worker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Journey.Messaging.Processing
{
    /// <summary>
    /// Processes incoming commands from the bus and routes them to the appropriate handlers.
    /// </summary>
    public class InMemoryCommandProcessor : ICommandHandlerRegistry, ICommandProcessor
    {
        private Dictionary<Type, ICommandHandler> handlers = new Dictionary<Type, ICommandHandler>();
        private readonly ITracer tracer;


        /// <summary>
        /// Initializes a new instance of the <see cref="CommandProcessor"/> class.
        /// </summary>
        /// <param name="receiver">The receiver to use. If the receiver is <see cref="IDisposable"/>, it will be
        ///  disposed.</param>
        /// <param name="serializer">The serializer to use for the message body.</param>
        public InMemoryCommandProcessor(ITracer tracer)
        {
            this.tracer = tracer;
        }

        /// <summary>
        /// Registers the specified command handler.
        /// </summary>
        public void Register(ICommandHandler commandHandler)
        {
            var genericHandler = typeof(ICommandHandler<>);
            var supportedCommandTypes =
                commandHandler
                .GetType()
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericHandler)
                .Select(i => i.GetGenericArguments()[0])
                .ToList();

            if (handlers.Keys.Any(registeredType => supportedCommandTypes.Contains(registeredType)))
                throw new ArgumentException("The command handled by the received handler already has a registered handler.");

            // Register this handler for each of the handled types.
            foreach (var commandType in supportedCommandTypes)
                this.handlers.Add(commandType, commandHandler);
        }

        /// <summary>
        /// Processes the message by calling the registered handler.
        /// </summary>        
        public void ProcessMessage(object payload)
        {
            ICommandHandler handler = null;

            var commandType = payload.GetType();
            if (this.handlers.TryGetValue(commandType, out handler))
            {
                this.HandleMessage(payload, handler);

                // There logging/tracing/auditing handlers
                if (this.handlers.TryGetValue(typeof(ICommand), out handler))
                    this.HandleMessage(payload, handler);
            }
            else
                throw new NotImplementedException("Command does not have handler!");
        }

        private void HandleMessage(object payload, ICommandHandler handler)
        {
            // Litle retry policy
            var attempts = 0;
            while (true)
            {
                try
                {
                    ((dynamic)handler).Handle((dynamic)payload);
                    break;
                }
                catch (Exception e)
                {
                    ++attempts;
                    if (attempts >= 3)
                        throw;

                    this.tracer.Notify(new List<string>
                    {
                        new string('-', 80),
                        string.Format(
                        "Handle command attempt number {0}. An exception happened while processing message through handler: {1}\r\n{2}", attempts, handler.GetType().Name, e),
                        new string('-', 80)
                    }
                    .ToArray());

                    if (attempts > 1)
                        Thread.Sleep(attempts * 1000);
                }
            }


            this.tracer.TraceAsync("Handled by " + handler.GetType().FullName);
        }
    }
}
