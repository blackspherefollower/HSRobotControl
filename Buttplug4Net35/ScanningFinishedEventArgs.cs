﻿using System;
using Buttplug4Net35.Messages;

namespace Buttplug4Net35
{
    /// <summary>
    /// Event wrapper for a Buttplug ScanningFinished message. Used when the server has completed all
    /// device scanning.
    /// </summary>
    public class ScanningFinishedEventArgs : EventArgs
    {
        /// <summary>
        /// The Buttplug ScanningFinished message.
        /// </summary>
        public readonly ScanningFinished Message;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScanningFinishedEventArgs"/> class.
        /// </summary>
        /// <param name="aMsg">A Buttplug ScanningFinished message.</param>
        public ScanningFinishedEventArgs(ScanningFinished aMsg)
        {
            Message = aMsg;
        }
    }
}