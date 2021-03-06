﻿using Journey.Messaging;
using System;

namespace Journey.EventSourcing
{
    public interface IEventStore<T> where T : IEventSourced
    {
        /// <summary>
        /// Tries to retrieve the event sourced entity.
        /// </summary>
        /// <param name="id">The id of the entity</param>
        /// <returns>The hydrated entity, or null if it does not exist.</returns>
        T Find(Guid id);

        /// <summary>
        /// Retrieves the event sourced entity.
        /// </summary>
        /// <param name="id">The id of the entity</param>
        /// <returns>The hydrated entity</returns>
        /// <exception cref="EntityNotFoundException">If the entity is not found.</exception>
        T Get(Guid id);

        /// <summary>
        /// Saves the event sourced entity in a distributed transaction that wraps 
        /// the database update, the database log and the message publishing.
        /// </summary>
        /// <param name="eventSourced">The entity.</param>
        /// <param name="message">The message that update the event sourced entity.</param>
        void Save(T eventSourced, IMessage message);
    }
}
