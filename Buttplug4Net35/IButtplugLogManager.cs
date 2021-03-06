﻿using System;

namespace Buttplug4Net35
{
    /// <summary>
    /// Interface for log managers. See <see cref="ButtplugLogManager"/> implementation for more info.
    /// </summary>
    public interface IButtplugLogManager
    {
        /// <summary>
        /// Called when a log message has been received.
        /// </summary>
        event EventHandler<ButtplugLogMessageEventArgs> LogMessageReceived;

        /// <summary>
        /// Log level to report.
        /// </summary>
        ButtplugLogLevel Level { set; }

        /// <summary>
        /// Gets a Buttplug logger for the specified type. Used for creating loggers specific to
        /// class types, so the types can be prepended to the log message for tracing.
        /// </summary>
        /// <param name="aType">Type that this logger will be for</param>
        /// <returns>Buttplug logger object</returns>
        IButtplugLog GetLogger(Type aType);
    }
}
