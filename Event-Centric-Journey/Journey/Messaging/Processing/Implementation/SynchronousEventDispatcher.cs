﻿using Infrastructure.CQRS.Worker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Infrastructure.CQRS.Messaging.Processing
{
    public class SynchronousEventDispatcher: IEventDispatcher
    {
        private IWorkerRoleTracer tracer;
        private Dictionary<Type, List<Tuple<Type, Action<Envelope>>>> handlersByEventType;
        private Dictionary<Type, Action<IEvent, string, string, string>> dispatchersByEventType;

        public SynchronousEventDispatcher(IWorkerRoleTracer tracer)
        {
            this.handlersByEventType = new Dictionary<Type, List<Tuple<Type, Action<Envelope>>>>();
            this.dispatchersByEventType = new Dictionary<Type, Action<IEvent, string, string, string>>();
            this.tracer = tracer;
        }

        public void DispatchMessage(IEvent @event, string messageId, string correlationId, string traceIdentifier)
        {
            Action<IEvent, string, string, string> dispatch;
            var wasHandled = false;

            // Invoke the generic handlers that have registered to handle IEvent directly
            if (this.dispatchersByEventType.TryGetValue(typeof(IEvent), out dispatch))
            {
                dispatch(@event, messageId, correlationId, traceIdentifier);
                wasHandled = true;
            }

            if (this.dispatchersByEventType.TryGetValue(@event.GetType(), out dispatch))
            {
                dispatch(@event, messageId, correlationId, traceIdentifier);
                
                if (!wasHandled)
                    wasHandled = true;
            }

            if (!wasHandled)
                this.tracer.Notify(string.Format(CultureInfo.InvariantCulture, "Event{0} does not have any registered handler.", traceIdentifier));
        }

        public void Register(IEventHandler handler)
        {
            var handlerType = handler.GetType();

            foreach (var invocationTuple in this.BuildHandlerInvocations(handler))
            {
                var envelopeType = typeof(Envelope<>).MakeGenericType(invocationTuple.Item1);

                List<Tuple<Type, Action<Envelope>>> invocations;
                if (!this.handlersByEventType.TryGetValue(invocationTuple.Item1, out invocations))
                {
                    invocations = new List<Tuple<Type, Action<Envelope>>>();
                    this.handlersByEventType[invocationTuple.Item1] = invocations;
                }

                invocations.Add(new Tuple<Type, Action<Envelope>>(handlerType, invocationTuple.Item2));

                if (!this.dispatchersByEventType.ContainsKey(invocationTuple.Item1))
                    this.dispatchersByEventType[invocationTuple.Item1] = this.BuildDispatchInvocation(invocationTuple.Item1);
            }
        }

        private IEnumerable<Tuple<Type, Action<Envelope>>> BuildHandlerInvocations(IEventHandler handler)
        {
            var interfaces = handler.GetType().GetInterfaces();

            var eventHandlerInvocations =
                interfaces
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                .Select(i => new { HandlerInterface = i, EventType = i.GetGenericArguments()[0] })
                .Select(e => new Tuple<Type, Action<Envelope>>(e.EventType, this.BuildHandlerInvocation(handler, e.HandlerInterface, e.EventType)));

            return eventHandlerInvocations;
        }

        private Action<Envelope> BuildHandlerInvocation(IEventHandler handler, Type handlerType, Type messageType)
        {
            var envelopeType = typeof(Envelope<>).MakeGenericType(messageType);

            var parameter = Expression.Parameter(typeof(Envelope));
            var invocationExpression =
                Expression.Lambda(
                    Expression.Block(
                        Expression.Call(
                            Expression.Convert(Expression.Constant(handler), handlerType),
                            handlerType.GetMethod("Handle"),
                            Expression.Property(Expression.Convert(parameter, envelopeType), "Body"))),
                parameter);

            return (Action<Envelope>)invocationExpression.Compile();
        }

        private Action<IEvent, string, string, string> BuildDispatchInvocation(Type eventType)
        {
            var eventParameter = Expression.Parameter(typeof(IEvent));
            var messageIdParameter = Expression.Parameter(typeof(string));
            var correlationIdParameter = Expression.Parameter(typeof(string));
            var traceIdParameter = Expression.Parameter(typeof(string));

            var dispatchExpression =
                Expression.Lambda(
                    Expression.Block(
                        Expression.Call(
                            Expression.Constant(this),
                            this.GetType().GetMethod("DoDispatchMessage", BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(eventType),
                            Expression.Convert(eventParameter, eventType),
                            messageIdParameter,
                            correlationIdParameter,
                            traceIdParameter)),
                    eventParameter,
                    messageIdParameter,
                    correlationIdParameter,
                    traceIdParameter);

            return (Action<IEvent, string, string, string>)dispatchExpression.Compile();
        }

        private void DoDispatchMessage<T>(T @event, string messageId, string correlationId, string traceIdentifier)
            where T : IEvent
        {
            var envelope = Envelope.Create(@event);
            envelope.MessageId = messageId;
            envelope.CorrelationId = correlationId;

            List<Tuple<Type, Action<Envelope>>> handlers;
            if (this.handlersByEventType.TryGetValue(typeof(T), out handlers))
            {
                foreach (var handler in handlers)
                {
                    this.tracer.Notify(string.Format(CultureInfo.InvariantCulture,
                                "Event {0} routed to handler '{1}' HashCode: {2}.", @event.GetHashCode(), handler.Item1.FullName, handler.GetHashCode()));

                    handler.Item2(envelope);

                    this.tracer.Notify(string.Format(CultureInfo.InvariantCulture, "Event {0} handled by {1} HashCode: {2}.", @event.GetHashCode(), handler.Item1.FullName, handler.GetHashCode()));
                }
            }
        }
    }
}
