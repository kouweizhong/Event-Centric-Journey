﻿using Infrastructure.CQRS.Messaging;
using System;
using System.Collections.Generic;

namespace Infrastructure.CQRS.EventSourcing
{
    /// <summary>
    /// A Saga aggregate that publishes commands to the bus.
    /// </summary>
    /// <remarks>
    /// <para>Feels ankward and possibly disrupting to store POCOs (Plane Old CLR Objects) in the <see cref="ISaga"/> aggregate 
    /// implementor. Maybe it would be better if instead of using current sate values (properties in C# and columns in the SQL Database),
    /// we use event sourcing.</para>
    /// </remarks>
    public abstract class Saga : EventSourced, ISaga
    {
        private readonly List<ICommand> commands = new List<ICommand>();

        protected Saga(Guid id) : base(id) 
        { }

        public IEnumerable<ICommand> Commands { get { return this.commands; } }

        protected void AddCommand<T>(T command) where T : ICommand
        {
            this.commands.Add(command);
        }
    }
}
