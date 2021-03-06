﻿using System;
using System.ComponentModel.DataAnnotations;

namespace Journey.EventSourcing
{
    public class Event
    {
        // Following could is very useful when rebuilding the read model from the event store, 
        // to avoid replaying every possible event in the system
        [StringLength(255)]
        public string EventType { get; set; }

        public string Payload { get; set; }

        public DateTime CreationDate { get; set; }

        public DateTime LastUpdateTime { get; set; }

        public Guid CorrelationId { get; set; }

        public Guid SourceId { get; set; }

        [StringLength(255)]
        public string SourceType { get; set; }

        public int Version { get; set; }

        public bool IsProjectable { get; set; }
    }
}




