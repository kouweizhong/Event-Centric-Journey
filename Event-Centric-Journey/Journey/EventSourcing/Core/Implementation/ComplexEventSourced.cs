﻿using Journey.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Journey.EventSourcing
{
    public abstract class ComplexEventSourced : EventSourced,
        IRehydratesFrom<EarlyEventReceived>,
        IRehydratesFrom<CorrelatedEventProcessed>
    {
        protected readonly IDictionary<string, int> lastProcessedEvents = new Dictionary<string, int>();
        protected readonly IList<IVersionedEvent> earlyReceivedEvents = new List<IVersionedEvent>(100);

        public ComplexEventSourced(Guid id)
            : base(id)
        { }

        public bool TryProcessWithGuaranteedIdempotency(IVersionedEvent @event)
        {
            var eventKey = this.GetEventKey(@event);
            var eventVersion = @event.Version;

            var lastProcessedEventVersion = this.lastProcessedEvents.TryGetValue(eventKey);

            if (eventVersion <= lastProcessedEventVersion)
            {
                // el evento ya fue procesado.
                return false;
            }
            else if (lastProcessedEventVersion == eventVersion - 1)
            {
                // el evento vino en el orden correcto
                ((dynamic)this).Process((dynamic)@event);
                base.Update(new CorrelatedEventProcessed
                    {
                        CorrelatedEventTypeName = eventKey,
                        CorrelatedEventVersion = eventVersion
                    });

                this.ProcessEarlyEventsIfApplicable();
            }
            else
            {
                // verificando que no se agregue el mismo evento con la misma version varias veces
                if (this.earlyReceivedEvents
                    .Where(e => e.Version == eventVersion
                           && this.GetEventKey(e) == eventKey)
                    .Any())
                    return false;

                // el evento vino prematuramente, se almacena para procesarlo en el orden correcto
                this.Update(new EarlyEventReceived
                {
                    Event = @event
                });
            }
            // El caso cuando el evento es muy nuevo todavia y falta otro anterior.
            return true;
        }

        public void Rehydrate(EarlyEventReceived e)
        {
            this.earlyReceivedEvents.Add((IVersionedEvent)e.Event);
        }

        public void Rehydrate(CorrelatedEventProcessed e)
        {
            // Marcamos como evento procesado en la lista de eventos procesados
            if (this.lastProcessedEvents.ContainsKey(e.CorrelatedEventTypeName))
                this.lastProcessedEvents[e.CorrelatedEventTypeName] = e.CorrelatedEventVersion;
            else
                this.lastProcessedEvents.Add(e.CorrelatedEventTypeName, e.CorrelatedEventVersion);

            // Verificar aqui si esta en la lista o no.
            var earlyEvent = this.earlyReceivedEvents
                .Where(x => this.GetEventKey(x) == e.CorrelatedEventTypeName
                                && x.Version == e.CorrelatedEventVersion)
                .FirstOrDefault();

            if (earlyEvent != null)
                this.earlyReceivedEvents.Remove(earlyEvent);
        }

        private void ProcessEarlyEventsIfApplicable()
        {
            // Después de cargar todo comprobamos que no exista un evento que no haya sido procesado.
            var eventsOnTime = new List<IVersionedEvent>();
            if (this.earlyReceivedEvents.Count > 0 && this.lastProcessedEvents.Count > 0)
            {
                // Recorremos todos los eventos que se recibieron prematuramente
                foreach (var early in this.earlyReceivedEvents)
                {
                    // obtenemos el nombre del evento prematuro
                    var earlyEventName = this.GetEventKey(early);

                    // verificamos si esta en la lista de eventos procesados un evento recibido prematuramente
                    if (this.lastProcessedEvents.ContainsKey(earlyEventName))
                    {
                        var lastProcessed = lastProcessedEvents[earlyEventName];
                        if (early.Version - 1 == lastProcessed)
                            eventsOnTime.Add(early);
                    }
                }

                foreach (var onTime in eventsOnTime)
                    this.TryProcessWithGuaranteedIdempotency(onTime);
                // se va a marcar aqui si si esta en la lista o no.
            }
        }

        private string GetEventKey(IVersionedEvent @event)
        {
            return @event.SourceType + "_" + @event.SourceId.ToString() + "_" + ((object)@event).GetType().FullName;
        }
    }
}
