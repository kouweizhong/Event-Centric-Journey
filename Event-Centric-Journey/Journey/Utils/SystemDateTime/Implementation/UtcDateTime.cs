﻿using System;

namespace Journey.Utils.SystemDateTime
{
    public class UtcDateTime : ISystemDateTime
    {
        public DateTime Now
        {
            get
            {
                return DateTime.UtcNow;
            }
        }

        public DateTimeOffset NowOffset
        {
            get
            {
                return DateTimeOffset.UtcNow;
            }
        }
    }
}
